using Godot;
using System;
using System.Collections.Generic;

namespace Game.Utils;

/// <summary>
/// Batches 2D sprites by texture using MultiMeshInstance2D. One node per unique texture.
/// Provides instance add/update/remove APIs keyed by texture and returns opaque instance IDs.
/// </summary>
public partial class BatchRenderer2D : Node
{
    private class Batch
    {
        public MultiMeshInstance2D Instance { get; }
        public MultiMesh Mesh { get; }
        public Texture2D Texture { get; }
        public CanvasItemMaterial Material { get; }

        public readonly Stack<int> FreeIndices = new();
        public int Count => Mesh.InstanceCount;

        public Batch(Texture2D texture)
        {
            Texture = texture ?? throw new ArgumentNullException(nameof(texture));
            Mesh = new MultiMesh();
            Mesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform2D;
            // Color/custom data disabled by default in Godot 4 C#; no properties required here
            Mesh.InstanceCount = 0;
            // Workaround engine expecting a Mesh for AABB: assign a unit QuadMesh
            var quad = new QuadMesh();
            quad.Size = new Vector2(1, 1);
            Mesh.Mesh = quad;
            Instance = new MultiMeshInstance2D
            {
                Texture = texture,
                Multimesh = Mesh,
            };
            Material = new CanvasItemMaterial();
            Instance.Material = Material;
        }

        public int Allocate()
        {
            if (FreeIndices.Count > 0)
            {
                return FreeIndices.Pop();
            }
            var idx = Mesh.InstanceCount;
            Mesh.InstanceCount = idx + 1;
            return idx;
        }

        public void Free(int index)
        {
            // Compacting not implemented; reuse indices via free list
            FreeIndices.Push(index);
            // Optionally hide by moving offscreen; here we zero the transform
            Mesh.SetInstanceTransform2D(index, new Transform2D());
        }
    }

    private readonly Dictionary<Texture2D, Batch> _batches = new();
    private readonly Dictionary<ulong, (Texture2D texture, int index)> _instanceLookup = new();
    private ulong _nextId = 1;

    public Node2D Parent2D { get; set; }

    public override void _Ready()
    {
        base._Ready();
        if (Parent2D == null && GetParent() is Node2D p2) Parent2D = p2;
    }

    private Batch GetOrCreateBatch(Texture2D texture)
    {
        if (!_batches.TryGetValue(texture, out var batch))
        {
            batch = new Batch(texture);
            _batches[texture] = batch;
            if (Parent2D != null)
            {
                Parent2D.AddChild(batch.Instance);
            }
            else
            {
                AddChild(batch.Instance);
            }
        }
        return batch;
    }

    public ulong AddInstance(Texture2D texture, Transform2D transform)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));
        var batch = GetOrCreateBatch(texture);
        int idx = batch.Allocate();
        batch.Mesh.SetInstanceTransform2D(idx, transform);
        var id = _nextId++;
        _instanceLookup[id] = (texture, idx);
        return id;
    }

    public void UpdateInstance(ulong id, Transform2D transform)
    {
        if (!_instanceLookup.TryGetValue(id, out var entry)) return;
        var batch = _batches[entry.texture];
        batch.Mesh.SetInstanceTransform2D(entry.index, transform);
    }

    public void RemoveInstance(ulong id)
    {
        if (!_instanceLookup.TryGetValue(id, out var entry)) return;
        _instanceLookup.Remove(id);
        var batch = _batches[entry.texture];
        batch.Free(entry.index);
    }
}


