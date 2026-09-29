namespace Hoboman.ViewModels;

public sealed class MainViewModel(RequestTreeViewModel tree, RequestEditorViewModel editor, ResponseViewModel response)
{
    public RequestTreeViewModel Tree => tree;

    public RequestEditorViewModel Editor => editor;

    public ResponseViewModel Response => response;
}
