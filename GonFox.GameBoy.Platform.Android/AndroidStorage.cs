namespace GonFox.GameBoy.Platform.Android;

// Where an Android host keeps its files, in the app's private internal folder.
public static class AndroidStorage
{
    public static string Root => Application.Context.FilesDir?.AbsolutePath
        ?? throw new InvalidOperationException("The app's files folder is not available.");
    public static string SavesDirectory => Path.Combine(Root, "Saves");
    public static string StatesDirectory => Path.Combine(Root, "States");
    public static string ResumeDirectory => Path.Combine(Root, "Resume");
    public static string LibraryDirectory => Path.Combine(Root, "Library");
}
