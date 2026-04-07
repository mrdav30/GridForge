# GridForge

![GridForge Icon](https://raw.githubusercontent.com/mrdav30/GridForge/main/icon.png)

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

## 📦 Install

```bash
dotnet add package GridForge
```

GridForge targets `netstandard2.1` and `net8.0` and builds on `FixedMathSharp`, `SwiftCollections`, and `MemoryPack`.

### Unity

Unity-specific integration lives in the separate [GridForge-Unity](https://github.com/mrdav30/GridForge-Unity) repository.

---

## 📖 Start With The Wiki

- [Wiki Home](https://github.com/mrdav30/GridForge/wiki/Home)
- [Getting Started](https://github.com/mrdav30/GridForge/wiki/Getting-Started)
- [Core Concepts](https://github.com/mrdav30/GridForge/wiki/Core-Concepts)
- [Common Workflows](https://github.com/mrdav30/GridForge/wiki/Common-Workflows)
- [Architecture Overview](https://github.com/mrdav30/GridForge/wiki/Architecture-Overview)
- [Recipes](https://github.com/mrdav30/GridForge/wiki/Recipes)
- [FAQ and Troubleshooting](https://github.com/mrdav30/GridForge/wiki/FAQ-and-Troubleshooting)

---

## 🧪 Local Validation

```bash
dotnet restore GridForge.sln
dotnet build GridForge.sln --configuration Debug
dotnet test GridForge.sln --configuration Debug --no-build
```

For benchmark discovery:

```bash
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
```

---

## 🤝 Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines and workflow details.

---

## 💬 Community & Support

For questions, discussions, or general support, join the official Discord community:

👉 **[Join the Discord Server](https://discord.gg/mhwK2QFNBA)**

For bug reports or feature requests, please open an issue in this repository.

We welcome feedback, contributors, and community discussion across all projects.

---

## 📄 License

GridForge is licensed under the MIT License. See [LICENSE](LICENSE), [NOTICE](NOTICE), and [COPYRIGHT](COPYRIGHT) for details.
