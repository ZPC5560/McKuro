using CommunityToolkit.Mvvm.ComponentModel;

namespace McKuro.ViewModels;

/// <summary>ViewModel 基类。</summary>
public abstract class ViewModelBase : ObservableObject
{
    public virtual void SetPageActive(bool active) { }
}
