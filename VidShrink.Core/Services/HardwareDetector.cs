using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;

namespace VidShrink.Core.Services;

public interface IHardwareDetector
{
    Task<IReadOnlyList<EncoderDescriptor>> DetectAvailableEncodersAsync(
        string? ffmpegPath = null,
        bool verifyWithTestEncode = true,
        CancellationToken cancellationToken = default);
}

public class HardwareDetector : EncoderDetector, IHardwareDetector
{
    public HardwareDetector(
        IFfmpegLocator? locator = null,
        IPlatformService? platformService = null,
        IProcessRunner? processRunner = null)
        : base(locator, platformService, processRunner)
    {
    }
}
