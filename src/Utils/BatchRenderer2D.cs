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

    // Banded batching: one batch per (texture, band)
    private readonly Dictionary<(Texture2D tex, int band), Batch> _batches = new();
    private readonly Dictionary<ulong, (Texture2D texture, int band, int index)> _instanceLookup = new();
    private ulong _nextId = 1;

    public Node2D Parent2D { get; set; }

    // Y-bucket configuration
    [Export] public int BandCount { get; set; } = 32;
    [Export] public float BandHeight { get; set; } = 320f; // pixels per band
    [Export] public float YMin { get; set; } = -10000f; // lowest world Y expected

    private readonly List<Node2D> _bandNodes = new();

    public override void _Ready()
    {
        base._Ready();
        if (Parent2D == null && GetParent() is Node2D p2) Parent2D = p2;
        EnsureBandNodes();
    }

    private void EnsureBandNodes()
    {
        if (_bandNodes.Count >= BandCount) return;
        for (int i = _bandNodes.Count; i < BandCount; i++)
        {
            var bandNode = new Node2D { Name = $"Band_{i:00}" };
            if (Parent2D != null) AddChildSafe(Parent2D, bandNode); else AddChildSafe(this, bandNode);
            _bandNodes.Add(bandNode);
        }
    }

    private int ComputeBand(float y)
    {
        var idx = (int)Mathf.Floor((y - YMin) / BandHeight);
        if (idx < 0) idx = 0; else if (idx >= BandCount) idx = BandCount - 1;
        return idx;
    }

    private Batch GetOrCreateBatch(Texture2D texture, int band)
    {
        var key = (texture, band);
        if (!_batches.TryGetValue(key, out var batch))
        {
            batch = new Batch(texture);
            _batches[key] = batch;
            EnsureBandNodes();
            Node parentNode;
            if (band >= 0 && band < _bandNodes.Count) parentNode = _bandNodes[band];
            else parentNode = Parent2D != null ? Parent2D : this;
            AddChildSafe(parentNode, batch.Instance);
        }
        return batch;
    }

    private void AddChildSafe(Node parent, Node child)
    {
        // Avoid "Parent node is busy" errors by deferring when tree is building
        parent.CallDeferred(Node.MethodName.AddChild, child);
    }

    public ulong AddInstance(Texture2D texture, Transform2D transform)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));
        var band = ComputeBand(transform.Origin.Y);
        var batch = GetOrCreateBatch(texture, band);
        int idx = batch.Allocate();
        batch.Mesh.SetInstanceTransform2D(idx, transform);
        var id = _nextId++;
        _instanceLookup[id] = (texture, band, idx);
        return id;
    }

    public void UpdateInstance(ulong id, Transform2D transform)
    {
        if (!_instanceLookup.TryGetValue(id, out var entry)) return;
        var newBand = ComputeBand(transform.Origin.Y);
        if (newBand != entry.band)
        {
            // Move to a different band within the same texture
            var oldBatch = _batches[(entry.texture, entry.band)];
            oldBatch.Free(entry.index);
            var newBatch = GetOrCreateBatch(entry.texture, newBand);
            int newIndex = newBatch.Allocate();
            newBatch.Mesh.SetInstanceTransform2D(newIndex, transform);
            _instanceLookup[id] = (entry.texture, newBand, newIndex);
        }
        else
        {
            var batch = _batches[(entry.texture, entry.band)];
            batch.Mesh.SetInstanceTransform2D(entry.index, transform);
        }
    }

    public void RelocateInstanceTexture(ulong id, Texture2D newTexture, Transform2D transform)
    {
        if (newTexture == null) return;
        if (!_instanceLookup.TryGetValue(id, out var entry)) return;
        // free old index
        var oldBatch = _batches[(entry.texture, entry.band)];
        oldBatch.Free(entry.index);
        // allocate in new batch
        var newBand = ComputeBand(transform.Origin.Y);
        var newBatch = GetOrCreateBatch(newTexture, newBand);
        int newIndex = newBatch.Allocate();
        newBatch.Mesh.SetInstanceTransform2D(newIndex, transform);
        _instanceLookup[id] = (newTexture, newBand, newIndex);
    }

    public void DebugPrintStats()
    {
        foreach (var kv in _batches)
        {
            var batch = kv.Value;
            int active = batch.Count - batch.FreeIndices.Count;
            GD.Print($"Batch band={kv.Key.band} tex='{kv.Key.tex.ResourcePath}' active={active} capacity={batch.Count}");
        }
    }

    public void RemoveInstance(ulong id)
    {
        if (!_instanceLookup.TryGetValue(id, out var entry)) return;
        _instanceLookup.Remove(id);
        var batch = _batches[(entry.texture, entry.band)];
        batch.Free(entry.index);
    }
}


