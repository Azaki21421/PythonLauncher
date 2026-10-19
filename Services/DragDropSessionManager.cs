namespace PythonRunnerApp.Services;

public static class DragDropSessionManager
{
    public static ScriptSession? CurrentDraggedSession { get; set; }
    public static MainPage? SourcePage { get; set; }

    public static void StartDrag(ScriptSession session, MainPage sourcePage)
    {
        CurrentDraggedSession = session;
        SourcePage = sourcePage;
    }

    public static void Clear()
    {
        CurrentDraggedSession = null;
        SourcePage = null;
    }
}
