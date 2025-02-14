# GridForge

**A high-performance, deterministic spatial grid management system for simulations and game development.**  

---

## 🚀 Key Features

- **Deterministic Execution** – Supports **lockstep simulation** and **fixed-point** arithmetic.
- **Optimized Grid Management** – **Low memory allocations, spatial partitioning, and fast queries**.
- **Multi-Layered Grid System** – **Dynamic, hierarchical, and persistent  grids**.
- **Node-Based Spatial Queries** – Retrieve **occupants, obstacles, and meta-data partitions** efficiently.
- **Custom Blockers & Partitions** – Define obstacles and attach metadata dynamically.
- **Framework Agnostic** – Works with **Unity, Lockstep Engines, and .NET-based frameworks**.

---

## 📦 Installation

### Non-Unity Projects

1. **Install via NuGet**:
   ```bash
   dotnet add package GridForge
   ```
2. **Or Download/Clone**:
   ```bash
   git clone https://github.com/YOUR-REPO/GridForge.git
   ```
3. **Include in Project**:
   - Add `GridForge` to your solution or reference its compiled DLL.

### Unity Integration

1. **Download the Unity Package**:
   - Obtain the latest **`GridForge.unitypackage`** from [Releases](https://github.com/YOUR-REPO/GridForge/releases).
2. **Import into Unity**:
   - Navigate to **Assets > Import Package > Custom Package...**.
   - Select the **`GridForge.unitypackage`** file.
3. **Verify Setup**:
   - Ensure the `GridForge` namespace is accessible.

---

## 📖 Library Overview

### **🗂 Core Components**

| Component | Description |
|-----------|------------|
| `GlobalGridManager` | Manages **grids, nodes, & spatial queries**. |
| `Grid` | Represents a **single grid** containing **nodes & scan cells**. |
| `Node` | Represents a grid position, storing **occupants, obstacles, & state**. |
| `ScanCell` | Handles **spatial indexing** for faster queries. |
| `GridTracer` | Efficiently retrieves **covered nodes, scan cells, & paths**. |
| `GridObstacleManager` | Manages **grid-wide obstacles** dynamically. |
| `GridOccupantManager` | Handles **occupant tracking & retrieval**. |
| `ScanManager` | Optimized **scan queries** for spatial lookups. |
| `Blockers` | Defines **dynamic and static** obstacles. |
| `Partitions` | Adds **meta-data and custom logic** to nodes. |
---

## 📖 Usage Examples

### **🔹 Creating a Grid**
```csharp
GridConfiguration config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
GlobalGridManager.TryAddGrid(config, out ushort gridIndex);
```

### **🔹 Querying a Grid for Nodes**
```csharp
Vector3d queryPosition = new Vector3d(5, 0, 5);
if (GlobalGridManager.TryGetGrid(queryPosition, out Grid grid))
{
    if (grid.TryGetNode(queryPosition, out Node node))
    {
        Console.WriteLine($"Node at {queryPosition} is {(node.IsOccupied ? "occupied" : "empty")}");
    }
}
```

### **🔹 Adding a Blocker**
```csharp
BoundingArea blockArea = new BoundingArea(new Vector3d(3, 0, 3), new Vector3d(5, 0, 5));
Blocker blocker = new Blocker(blockArea);
blocker.ApplyBlockage();
```

### **🔹 Attaching a Partition to a Node**
```csharp
if (GlobalGridManager.TryGetGrid(queryPosition, out Grid grid) && grid.TryGetNode(queryPosition, out Node node))
{
    PathPartition partition = new PathPartition();
    partition.Setup(node.GlobalCoordinates);
    node.AddPartition(partition);
}
```

### **🔹 Scanning for Nearby Occupants**
```csharp
Vector3d scanCenter = new Vector3d(0, 0, 0);
Fixed64 scanRadius = (Fixed64)5;
foreach (INodeOccupant occupant in ScanManager.ScanRadius(scanCenter, scanRadius))
{
    Console.WriteLine($"Found occupant at {occupant.WorldPosition}");
}
```

## 🎮 Unity Debugging Tools

GridForge includes **editor utilities** for debugging:

- **GridDebugger** – Visualizes **grids, nodes, and selected areas**.
- **GridTracer Debuging** – Helps debug **line-of-sight & navigation**.
- **Blocker Editor** – Allows **visual blocker placement** via Unity Inspector.

---

## 🧪 Testing and Validation

GridForge includes **comprehensive unit tests**.

Run tests with:
```bash
dotnet test
```

---

## 🔄 Compatibility

- **.NET Framework** 4.8+
- **Unity 2020+**
- **Supports FixedMathSharp for deterministic precision**

---

## 📄 License

This project is licensed under the MIT License - see the `LICENSE` file for details.

---

## 👥 Contributors

- **David Oravsky** - Lead Developer
- **Contributions Welcome!** Open a PR or issue.

---

## 📧 Contact

For questions or support, open an issue on GitHub.
