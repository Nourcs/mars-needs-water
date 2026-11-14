using System.IO;
using UnityEditor;

namespace MarsNeedsWater.Earth.Editor
{
    /// <summary>
    /// Lets tools outside Unity ask for a refresh (and so a recompile) without the editor window having focus:
    /// create Temp/refresh.request and it is picked up within a second, unless the game is playing.
    /// </summary>
    [InitializeOnLoad]
    static class AutoRefresh
    {
        const string Request = "Temp/refresh.request";
        static double next;

        static AutoRefresh() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Request)) return;
            File.Delete(Request);
            AssetDatabase.Refresh();
        }
    }
}
