using System;
using System.Threading.Tasks;

namespace VidShrink.App.Services;

public interface IWindowManager
{
    Task ShowSettingsDialogAsync();
}
