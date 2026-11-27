namespace SaveSpace.Views
{
    public enum PageKind
    {
        Home,
        Notes,
        Links,
        Images,
        Videos,
        Documents,
        Audio,
        Favorites,
        Folders,
        Trash,
        Settings,
        Search,
        LinkedFolder
    }

    public interface IRefreshable
    {
        void Refresh();
    }
}
