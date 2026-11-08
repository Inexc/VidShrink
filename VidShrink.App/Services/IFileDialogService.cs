using System.Collections.Generic;
using System.Threading.Tasks;

namespace VidShrink.App.Services;

public interface IFileDialogService
{
    Task<IReadOnlyList<string>> OpenVideoFilesAsync();
    Task<string?> OpenFfmpegExecutableAsync();
    Task<string?> OpenFolderAsync(string? initialDirectory = null);
}
