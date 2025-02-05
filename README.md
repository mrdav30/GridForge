# GridForge

**A high-performance, framework-agnostic spatial grid management system for deterministic simulations and game development.**  
Optimized for low allocation, high precision, and cross-engine compatibility.

---

## 🚀 Key Features

- **Deterministic Grid Management** – Ensures **lockstep simulation compatibility**.
- **Highly Optimized** – Low time complexity and **minimal allocations**.
- **Framework Agnostic** – Can be used in **Unity**, **Lockstep engines**, or **custom frameworks**.
- **Multi-Layered Grid System** – Supports **dynamic, persistent, and hierarchical grids**.
- **Node-Based Spatial Queries** – Retrieve **occupants, obstacles, and available paths** efficiently.
- **Customizable Blockers** – Define **grid obstacles** dynamically.
- **Partitioned Nodes** – Attach **meta-data partitions** to nodes for **custom behavior**.

---

## 📦 Installation

### Non-Unity Projects

1. **Install via NuGet** (if published):
   ```bash
   dotnet add package GridForge
   ```
2. **Or Download/Clone**:
   ```bash
   git clone https://github.com/YOUR-REPO/GridForge.git
   ```
3. **Include in Project**:
   - Add the `GridForge` project or its DLLs to your solution.

### Unity Integration

1. **Download the Package**:
   - Get the latest **`GridForge.unitypackage`** from [Releases](https://github.com/YOUR-REPO/GridForge/releases).
2. **Import into Unity**:
   - Navigate to **Assets > Import Package > Custom Package...**.
   - Select the **`GridForge.unitypackage`** file.
3. **Verify the Integration**:
   - Ensure the `GridForge` namespace is accessible.

---

## 📖 Library Overview

### **🗂 Core Components**

| Component | Description |
|-----------|------------|
| `GlobalGridManager` | Centralized manager for **grids & nodes**. Handles allocation & retrieval. |
| `Grid` | Represents an **individual grid** with **nodes** and **querying methods**. |
| `Node` | Represents a **single grid space**, stores **occupants & state**. |
| `ScanCell` | Handles **spatial indexing** for performance optimizations. |
| `GridConfiguration` | Defines grid parameters like **size, resolution, and persistence**. |
| `Blockers` | Prevent movement through **designated grid spaces**. |
| `Partitions` | Attach **custom behaviors** and **metadata** to nodes dynamically. |

---

## 📖 Usage Examples

### **🔹 Creating a Grid**
```csharp
GridConfiguration config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
GlobalGridManager.AddGrid(config);
```

### **🔹 Querying a Grid for Nodes**
```csharp
Vector3d queryPosition = new Vector3d(5, 0, 5);
if (GlobalGridManager.GetGrid(queryPosition, out Grid grid))
{
    if (grid.GetNode(queryPosition, out Node node))
    {
        Console.WriteLine($"Node at {queryPosition} is {(node.IsOccupied ? "occupied" : "empty")}");
    }
}
```

### **🔹 Adding a Blocker**
```csharp
BoundingArea blockArea = new BoundingArea(new Vector3d(3, 0, 3), new Vector3d(5, 0, 5));
IBlocker blocker = new Blocker(blockArea);
blocker.ApplyBlockage();
```

### **🔹 Attaching a Partition to a Node**
```csharp
if (GlobalGridManager.GetGrid(queryPosition, out Grid grid) && grid.GetNode(queryPosition, out Node node))
{
    PathPartition partition = new PathPartition();
    partition.Setup(node.GlobalCoordinates);
    node.AddPartition(partition);
}
```

### **🔹 Debugging with `GridDebugger`**
```csharp
[ExecuteAlways]
public class GridDebugComponent : MonoBehaviour
{
    void OnDrawGizmos()
    {
        GridDebugger.DrawGizmos();
    }
}
```

---

## 🎮 Unity Debugging Tools

GridForge includes **editor utilities** for debugging:

- **GridDebugger** – Visualizes **grids, nodes, and selected areas**.
- **GridLineTracer** – **Draws paths** for line-of-sight & navigation debugging.
- **Blocker Editor** – Configures **blockers via Unity Inspector**.

---

## 🧪 Testing and Validation

GridForge includes **unit tests** to ensure accuracy.

Run tests with:
```bash
dotnet test
```

---

## 🔄 Compatibility

- **.NET Framework** 4.7.2+
- **Unity 2020+**
- **Supports FixedMathSharp for deterministic precision**

---

## 📄 License

This project is licensed under the MIT License - see the `LICENSE` file for details.

---

## 👥 Contributors

- **Your Name** - Lead Developer
- **Contributions Welcome!** Open a PR or issue.

---

## 📧 Contact

For questions or support, open an issue on GitHub.
