using System;

namespace SpawnAnalyzer.UI;

public class Selection<T> where T: ISelectable
{
    private T? currentSelection;

    public T? CurrentSelection { 
        get => currentSelection; 
        set {
            if (currentSelection is not null)
                currentSelection.Selected = false;

            currentSelection = value;

            if (currentSelection is not null)
                currentSelection.Selected = true;

            OnSelectionChanged?.Invoke(currentSelection);
        }
    }

    public event Action<T?>? OnSelectionChanged;

    public Selection() {}
}

public interface ISelectable
{
    bool Selected { get; set; }
}