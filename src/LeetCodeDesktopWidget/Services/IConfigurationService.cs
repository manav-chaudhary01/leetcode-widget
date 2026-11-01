using System.Threading.Tasks;
using LeetCodeDesktopWidget.Models;

namespace LeetCodeDesktopWidget.Services;

public interface IConfigurationService
{
    string ConfigFilePath { get; }
    Task<WidgetConfig> LoadConfigurationAsync();
    Task SaveConfigurationAsync(WidgetConfig config);
}
