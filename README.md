# GridForge

![SwiftCollections Icon](https://raw.githubusercontent.com/mrdav30/GridForge/main/icon.png)

[![.NET CI](https://github.com/mrdav30/GridForge/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/mrdav30/GridForge/actions/workflows/dotnet.yml)

**A high-performance, deterministic voxel grid system for spatial partitioning, simulation, and game development.**

Lightweight, framework-agnostic, and optimized for lockstep engines.

---

## 🚀 Key Features

- **Voxel-Based Spatial Partitioning** – Build efficient 3D **voxel grids** with fast access & updates.
- **Deterministic & Lockstep Ready** – Designed for **synchronized multiplayer** and physics-safe environments.
- **ScanCell Overlay System** – Accelerated **proximity and radius queries** using spatial hashing.
- **Dynamic Occupancy & Obstacle Tracking** – Manage **moving occupants, dynamic obstacles**, and voxel metadata.
- **Minimal Allocations & Fast Queries** – Built with **SwiftCollections** and **FixedMathSharp** for optimal performance.
- **Framework Agnostic** – Works in **Unity**, **.NET**, **lockstep engines**, and **server-side simulations**.
- **Multi-Layered Grid System** – **Dynamic, hierarchical, and persistent grids**.

---

## ❓ Why GridForge?

GridForge is built for developers who need **deterministic**, **high-performance**, and **framework-agnostic** spatial grids. Whether you're building a **lockstep multiplayer game**, a **server-driven simulation**, or a **high-fidelity physics system**, GridForge provides the tools to manage voxelized spatial data with predictable and efficient results — all without relying on any specific engine.

## 📦 Installation

### Non-Unity Projects

1. **Install via NuGet**:

   ```bash
   dotnet add package GridForge
   ```

2. **Or Download/Clone**:

   ```bash
   git clone https://github.com/mrdav30/GridForge.git
   ```

3. **Include in Project**:
   - Add `GridForge` to your solution or reference its compiled DLL.

### Unity

GridForge is maintained as a separate Unity package. For Unity-specific implementations, refer to:

🔗 [GridForge-Unity Repository](https://github.com/mrdav30/GridForge-Unity).

---

## 🧩 Dependencies

GridForge depends on the following libraries:

- [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp)
- [SwiftCollections](https://github.com/mrdav30/SwiftCollections)

These dependencies are automatically included when installing.

---

## 📖 Library Overview

### **🗂 Core Components**

| Component | Description |
| ----------- | ------------ |
| `GlobalGridManager` | Manages **VoxelGrids**, global spatial queries, and grid registration. |
| `VoxelGrid` | Represents a **single grid** containing **voxels & scan cells**. |
| `Voxel` | Represents a voxel **cell** with occupant, obstacle, and partition data.. |
| `ScanCell` | Handles **spatial indexing** for fast neighbor and radius queries.. |
| `GridTracer` | Trace lines, areas, and **paths** across voxels and scan cells. |
| `GridObstacleManager` | Manage **dynamic grid obstacles** at runtime.. |
| `GridOccupantManager` | Manage and query **occupants** in voxels. |
| `GridScanManager` | Optimized **scan queries** (radius, box, path, etc). |
| `Blockers` | Define static or dynamic voxel blockers. |
| `Partitions` | Adds **meta-data** and **custom logic** to voxels. |

---

## 📖 Usage Examples

### **🔹 Creating a Grid**

```csharp
GridConfiguration config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
```

### **🔹 Querying a Grid for Voxels**

```csharp
Vector3d queryPosition = new Vector3d(5, 0, 5);
if (GlobalGridManager.TryGetGridAndVoxel(queryPosition, out VoxelGrid grid, out Voxel voxel))
 Console.WriteLine($"Voxel at {queryPosition} is {(voxel.IsOccupied ? "occupied" : "empty")}");
}
```

### **🔹 Adding a Blocker**

```csharp
BoundingArea blockArea = new BoundingArea(new Vector3d(3, 0, 3), new Vector3d(5, 0, 5));
Blocker blocker = new Blocker(blockArea);
blocker.ApplyBlockage();
```

### **🔹 Attaching a Partition to a Voxel**

```csharp
if (GlobalGridManager.TryGetGrid(queryPosition, out VoxelGrid grid, out Voxel voxel))
{
    PathPartition partition = new PathPartition();
    partition.Setup(voxel.GlobalVoxelIndex);
    voxel.AddPartition(partition);
}
```

### **🔹 Scanning for Nearby Occupants**

```csharp
Vector3d scanCenter = new Vector3d(0, 0, 0);
Fixed64 scanRadius = (Fixed64)5;
foreach (IVoxelOccupant occupant in ScanManager.ScanRadius(scanCenter, scanRadius))
{
    Console.WriteLine($"Found occupant at {occupant.WorldPosition}");
}
```

---

## 🧪 Testing and Validation

GridForge includes **comprehensive unit tests** and a BenchmarkDotNet performance suite.

Run tests with:

```bash
dotnet test
```

Run benchmarks with:

```bash
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- all --filter '*'
```

Benchmark reports are written to `BenchmarkDotNet.Artifacts/results/`.

---

## 🔄 Compatibility

- **.NET Standard** 2.1+
- **.NET** 8+
- **Unity** 2020+ (via - [GridForge-Unity](https://github.com/mrdav30/GridForge-Unity))

---

## 🤝 Contributing

We welcome contributions! Please see our [CONTRIBUTING](https://github.com/mrdav30/GridForge/blob/main/CONTRIBUTING.md) guide for details on how to propose changes, report issues, and interact with the community.

---

## 👥 Contributors

- **David Oravsky** - Lead Developer
- **Contributions Welcome!** Open a PR or issue.

---

## 💬 Community & Support

For questions, discussions, or general support, join the official Discord community:

👉 **[Join the Discord Server](https://discord.gg/mhwK2QFNBA)**

For bug reports or feature requests, please open an issue in this repository.

We welcome feedback, contributors, and community discussion across all projects.

---

## License

This project is licensed under the MIT License.

See the following files for details:

- LICENSE – standard MIT license
- NOTICE – additional terms regarding project branding and redistribution
- COPYRIGHT – authorship information
