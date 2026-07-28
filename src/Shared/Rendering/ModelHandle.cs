using Raylib_cs;

namespace ColdAudit.Shared.Rendering;

public sealed class ModelHandle : IDisposable
{
    public Model Model { get; private set; }
    public bool IsLoaded { get; private set; }

    public void Load(string path)
    {
        Unload();
        Model = Raylib.LoadModel(path);
        IsLoaded = true;
    }

    public void Unload()
    {
        if (!IsLoaded)
        {
            return;
        }

        Raylib.UnloadModel(Model);
        IsLoaded = false;
    }

    public void Dispose() => Unload();
}
