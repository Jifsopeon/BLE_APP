using BLE_APP.Models;
using CommunityToolkit.Mvvm.Input;

namespace BLE_APP.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}