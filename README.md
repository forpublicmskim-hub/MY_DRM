# DRM

Production-oriented DRM lifecycle skeleton for .NET 10. The current milestone establishes boundaries and safe orchestration; it is not a complete DRM product and does not yet provide real cryptography, licensing, tamper resistance, or kernel enforcement.

Build and test:

```powershell
dotnet build Drm.slnx
dotnet test Drm.slnx
```

See `docs/architecture.md` for responsibilities and planned increments.
