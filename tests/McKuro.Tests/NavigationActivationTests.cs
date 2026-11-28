using McKuro.ViewModels;
using Xunit;

namespace McKuro.Tests;

public class MockViewModel : ViewModelBase
{
    public bool? LastActive { get; private set; }
    public int SetActiveCount { get; private set; }
    public override void SetPageActive(bool active)
    {
        LastActive = active;
        SetActiveCount++;
    }
}

public class BaseViewModelImpl : ViewModelBase
{
    // Uses default implementation
}

public class NavigationActivationTests
{
    [Fact]
    public void ViewModelBase_DefaultImplementation_DoesNotThrow()
    {
        var vm = new BaseViewModelImpl();
        // Should not throw
        vm.SetPageActive(true);
        vm.SetPageActive(false);
    }

    [Fact]
    public void MockViewModel_TracksActivationState()
    {
        var vm = new MockViewModel();
        
        vm.SetPageActive(false);
        Assert.Equal(false, vm.LastActive);
        Assert.Equal(1, vm.SetActiveCount);

        vm.SetPageActive(true);
        Assert.Equal(true, vm.LastActive);
        Assert.Equal(2, vm.SetActiveCount);
    }

    [Fact]
    public void NavigationContract_Emulation()
    {
        var oldVm = new MockViewModel();
        var newVm = new MockViewModel();

        // Emulate MainWindowViewModel's NavigateTo call sequence
        oldVm.SetPageActive(false); // Leaving old
        newVm.SetPageActive(true);  // Entering new

        Assert.Equal(false, oldVm.LastActive);
        Assert.Equal(1, oldVm.SetActiveCount);
        Assert.Equal(true, newVm.LastActive);
        Assert.Equal(1, newVm.SetActiveCount);
    }
}
