using Godot;

namespace Game.Utils;

/// <summary>
/// Simple on-screen performance overlay showing FPS and memory usage.
/// Implemented as a singleton for easy access and to avoid duplicates.
/// </summary>
public partial class StatsOverlay : SingletonNode<StatsOverlay>
{
    [Export] public bool OverlayEnabled = true;
    [Export] public float UpdatesPerSecond = 4.0f; // how often to refresh the text
    [Export] public int MarginX = 8;
    [Export] public int MarginY = 8;
    [Export] public bool ShowObjectCount = false;
    [Export] public int GraphWidth = 220;
    [Export] public int GraphHeight = 60;
    [Export] public int GraphSampleCount = 240;
    [Export] public float GraphMaxMs = 50.0f; // vertical scale
    [Export] public Color GraphColor = new Color(0.2f, 0.9f, 0.2f);
    [Export] public bool Show60HzLine = true;
    [Export] public Color Graph60HzLineColor = new Color(1f, 1f, 0f, 0.4f);

    private Label _label;
    private CanvasLayer _layer;
    private PanelContainer _panel;
    private VBoxContainer _content;
    private MsPerFrameGraph _graph;
    private float _accumulator;

    public override void _Ready()
    {
        base._Ready();

        // Build a tiny UI overlay in code
        _layer = new CanvasLayer { Name = "StatsOverlayLayer" };
        _layer.Layer = 100; // ensure on top
        AddChild(_layer);

        _panel = new PanelContainer { Name = "StatsPanel" };
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _panel.OffsetTop = MarginY;
        _panel.OffsetLeft = MarginX;
        _layer.AddChild(_panel);

        _content = new VBoxContainer { Name = "Content" };
        _content.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.AddChild(_content);

        _label = new Label { Name = "StatsLabel" };
        _label.MouseFilter = Control.MouseFilterEnum.Ignore;
        _label.HorizontalAlignment = HorizontalAlignment.Left;
        _content.AddChild(_label);

        _graph = new MsPerFrameGraph
        {
            Name = "MsPerFrameGraph",
            LineColor = GraphColor,
            ShowTargetLine = Show60HzLine,
            TargetLineColor = Graph60HzLineColor,
            TargetLineMs = 1000f / 60f,
            MaxMs = GraphMaxMs
        };
        _graph.CustomMinimumSize = new Vector2(GraphWidth, GraphHeight);
        _graph.Initialize(GraphSampleCount);
        _content.AddChild(_graph);

        UpdateTextImmediate();
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!OverlayEnabled)
        {
            if (_panel.Visible)
                _panel.Visible = false;
            return;
        }

        if (!_panel.Visible)
            _panel.Visible = true;

        // Feed graph every frame for accuracy
        if (_graph != null)
        {
            _graph.MaxMs = GraphMaxMs;
            _graph.AddSample((float)delta * 1000.0f);
            _graph.QueueRedraw();
        }

        var interval = 1.0f / Mathf.Max(1.0f, UpdatesPerSecond);
        _accumulator += (float)delta;
        if (_accumulator < interval)
            return;

        _accumulator = 0.0f;
        UpdateTextImmediate();
    }

    private void UpdateTextImmediate()
    {
        // FPS
        var fps = Engine.GetFramesPerSecond();

        // Memory (bytes). Some platforms expose only MemoryStatic.
        var memStatic = (double)Performance.GetMonitor(Performance.Monitor.MemoryStatic);
        var memTotalMB = memStatic / (1024.0 * 1024.0);

        string text = $"FPS: {fps:0}  |  Mem: {memTotalMB:0.0} MB";

        // Entities
        try
        {
            var em = Game.Universe.EntityManager.Instance;
            if (em != null)
            {
                int total = em.EntityCount;
                int active = em.ActiveEntityCount;
                text += $"  |  Entities: {total} ({active} active)";
            }
        }
        catch { /* EntityManager may not be ready in tool mode */ }

        if (ShowObjectCount)
        {
            var objects = (double)Performance.GetMonitor(Performance.Monitor.ObjectCount);
            text += $"  |  Objects: {objects:0}";
        }

        if (_label != null)
            _label.Text = text;
    }
}

/// <summary>
/// Lightweight line graph control for ms-per-frame visualization.
/// </summary>
public partial class MsPerFrameGraph : Control
{
    public float MaxMs = 50.0f;
    public Color LineColor = new Color(0.2f, 0.9f, 0.2f);
    public bool ShowTargetLine = true;
    public Color TargetLineColor = new Color(1f, 1f, 0f, 0.4f);
    public float TargetLineMs = 16.67f;

    private float[] _samples;
    private int _head;
    private int _count;

    public void Initialize(int capacity)
    {
        if (capacity < 2) capacity = 2;
        _samples = new float[capacity];
        _head = 0;
        _count = 0;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void AddSample(float ms)
    {
        if (_samples == null || _samples.Length == 0) return;
        _samples[_head] = ms;
        _head = (_head + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;
    }

    public override void _Draw()
    {
        if (_samples == null || _count < 2) return;

        var size = GetRect().Size;
        var width = size.X;
        var height = size.Y;
        if (width <= 1 || height <= 1 || MaxMs <= 0.0f) return;

        // Optional 60 Hz baseline
        if (ShowTargetLine)
        {
            float y60 = height - Mathf.Clamp(TargetLineMs, 0.0f, MaxMs) * (height / MaxMs);
            DrawLine(new Vector2(0, y60), new Vector2(width, y60), TargetLineColor, 1.0f);
        }

        int capacity = _samples.Length;
        int n = _count;
        float stepX = (n <= 1) ? width : (width - 1.0f) / (n - 1);
        float scaleY = height / MaxMs;

        // Draw as a polyline via per-segment lines from oldest to newest
        for (int i = 1; i < n; i++)
        {
            // Oldest on the left, newest on the right
            int idxPrev = (capacity + _head - n + (i - 1)) % capacity;
            int idxCurr = (capacity + _head - n + i) % capacity;

            float msPrev = Mathf.Clamp(_samples[idxPrev], 0.0f, MaxMs);
            float msCurr = Mathf.Clamp(_samples[idxCurr], 0.0f, MaxMs);

            float x0 = (i - 1) * stepX;
            float x1 = i * stepX;
            float y0 = height - msPrev * scaleY;
            float y1 = height - msCurr * scaleY;

            DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), LineColor, 1.0f);
        }
    }
}


