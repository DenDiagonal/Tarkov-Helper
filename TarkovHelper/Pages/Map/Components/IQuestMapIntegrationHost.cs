using TarkovHelper.Models;

namespace TarkovHelper.Pages.Map.Components;

public interface IQuestMapIntegrationHost
{
    void NavigateToTaskOnMap(TarkovTask? task, string? targetMapKey = null);
}
