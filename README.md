# Godot GOAP & ECS Game Tech Demo

This project is a high-performance technical demonstration of Goal-Oriented Action Planning (GOAP)  integrated with a custom Entity Component System (ECS) in Godot 4.5 Mono with C#. It is designed to see how complex AI systems and massive entity counts can scale in a real-time survival-style environment.

![Many agents demo](many_people_demo.png)
*500+ AI planning agents planning, chopping trees, crafting campfires, cooking food, and building beds.*

## What's Inside?

*   **High-Throughput GOAP Planner:** An advanced A* planner that can generate over 60,000 action plans per second, running in parallel across available CPU cores.
*   **Custom ECS Architecture:** A lightweight, data-oriented ECS implementation capable of updating 150,000+ simple entities while keeping frame times manageable.
*   **Spatial Partitioning:** A custom QuadTree implementation optimized for fast spatial queries and resource lookups.
*   **Benchmarking Tools:** Built-in harnesses to stress-test the system and visualize performance metrics (press `B` in-game).

## Performance

### Hardware Context
These benchmarks were recorded on the following machine:
*   **CPU:** AMD Ryzen 9 7950X (32 Threads @ 5.88 GHz)
*   **GPU:** NVIDIA GeForce RTX 5070 Ti
*   **RAM:** 94 GB
*   **OS:** CachyOS (Linux Kernel 6.17)

### ECS Scalability
The ECS logic update loop is decoupled from rendering, allowing the simulation to remain stable even as entity counts push into the hundreds of thousands.

![ECS Performance Graph](ECS_Perf_Graph.png)

### GOAP AI Throughput
The AI planner scales linearly with the number of concurrent agents. In my tests, the system achieved a throughput of approximately **62,000 agents per second**, with an average planning cost of just **0.045ms** per plan.

![GOAP Performance Graph](GOAP_Perf_Graph.png)

## Getting Started

1.  Open the project in **Godot 4.5** (C# edition).
2.  Build the solution to restore NuGet packages.
3.  Run `scenes/main.tscn`.
4.  Press `B` at any time to run the benchmark suite yourself.

## Where to find Benchmark Results
If you run the benchmarks yourself, you'll see in the root of the godot project there will be a file called `performance_benchmark_*.csv` which will contain all the data retrieved during the benchmark

## Visual Examples

### Simple scenario with 1 tree, 3 raw food items, and a single intelligent agent.
![4K Screenshot](4K_Screenshot_Demo.png)
The agent in the screenshot is executing a plan to satisfy its hunger goal



## Development Methodology

This project was built through iterative profile-driven optimization. Architectural decisions were made based on my cross-engine experience in the Unity Engine, while low-level optimization techniques were researched and implemented systematically.

Development process:
1. **Write System** I would create an initial working version of a system
2. **Profile** using JetBrains Rider's timeline profiler, I'd identify bottlenecks. These were usually things like requesting the same data every frame when it should have been cached, or poorly thought out algorithm implementations.
3. **Research** I'd research optimization techniques for hot-path methods (AI-assisted research and review of existing code in Cursor Editor with GPT 5.1 Codex High)
4. **Apply** rewriting code with better patterns (zero-allocation, caching, optimized data structures)
5. **Verify** improvements through re-profiling and benchmark comparison
6. **Repeat** until I was satisfied with performance. I didn't have specific performance goals, I was just trying to maximize the amount of entities and GOAP agents that I could use at the same time.

## Architecture

### Entity Component System (ECS)

Entities in this system are not defined by inheritance. Instead, each `Entity` is a lightweight container that gains its identity and behavior through **composition**—attaching various `IComponent` implementations at runtime.

**Blueprints for Composition:** The `EntityBlueprint` class provides a data-driven way to define entity archetypes. Blueprints can be layered using a base-chaining pattern, where a derived blueprint inherits tags, components, and mutators from its parent. For example:

```
BaseEntity → Entity2D → EmbodiedEntity → NPCBase → Intelligent
```

Each layer adds or overrides components. A `Wanderer` NPC simply derives from `NPCBase` and adds a `WanderBehavior` component, while an `Intelligent` NPC adds `UtilityGoalSelector` and `AIGoalExecutor` components instead. This approach avoids deep class hierarchies and allows for flexible, mix-and-match entity construction. 

 For example I was able to seperate the **Utility based AI Goal selection** from the **GOAP based AI goal planning and execution** and still have them share information between the different systems.


### Custom Entity Renderer

To support hundreds of thousands of entities, the system bypasses Godot's node tree for rendering. Instead of creating a `Sprite2D` node for each entity and dealing with the overhead that would cause, all sprites are drawn by a single `CustomEntityRenderEngine` node.

This immediate-mode renderer:
- Maintains a flat list of sprite data (texture, position, rotation, scale, color)
- Sorts sprites by Y-position each frame for correct painter's-order depth
- Draws all sprites in a single `_Draw()` pass using Godot's own low-level drawing API
- Supports per-sprite shader overlays for visual effects like hunger/health indicators

By avoiding node instantiation entirely, the renderer can handle massive entity counts without the memory and CPU overhead that comes with Godot's scene tree.

### GOAP AI Planner

The planner uses a two-stage approach to efficiently search for action plans:

**Stage 1: Relevance Pruning (Backward Dependency Analysis):**  
Before searching, the planner analyzes all available steps to determine which ones are *relevant* to the goal. It starts by identifying steps whose effects directly satisfy goal facts, then works backward to find steps that enable those goal-achieving steps (transitive closure). Any step that doesn't contribute to the goal directly or indirectly is pruned from the search space.

In practice, this pruning is significant. The current demo has **23 total possible steps**. But the dependency analysis can reduce it to:
- **~11 steps (63.2% reduction)** for a "satisfy hunger" goal: `go to beef -> pick up beef -> bring to campfire -> retrieve cooked food from campfire -> eat` (may include steps to chop a tree, collect sticks, and build a campfire to cook at)
- **~7 steps (70.6% reduction)** for a "go to sleep sleep" goal: `go to bed -> sleep` (may include steps to chop a tree, collect sticks, and build a bed to sleep in)


**Stage 2: Forward A\* Search:**  
Since step 1 created an optimized step set, the planner runs a standard A\* search from the initial world state toward the goal state. Each node in the search represents a world state, and edges are the pruned steps. The heuristic estimates remaining cost based on unsatisfied goal facts. 

**Numeric Reasoning via Implicit Requirements:**  
To handle integer-based facts (like inventory counts), the planner derives *implicit requirements* from producer steps. If a goal requires "has cooked food" and the step to cook food requires "raw food count >= 1", the planner infers that acquiring raw food is a sub-goal. This allows the heuristic to intelligently guide the search toward states that satisfy numeric preconditions, even when they aren't explicit in the original goal.

The result is a planner that scales linearly with agent count and can generate over 10,000 plans per second on modern hardware. 

 

### Attribution Notes
The GOAP pruning strategy was implemented after it was suggested by GPT 5.1 Codex High.

Many optimization techniques (buffer reuse, shader parameter caching, lazy sorting) came from research based on profiler data.

The custom renderer and ECS architecture specifically came from my own experience in creating similar systems in Unity Engine and translated easily into Godot C#. 

