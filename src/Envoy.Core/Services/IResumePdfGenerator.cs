using Envoy.Core.Models;

namespace Envoy.Core.Services;

public interface IResumePdfGenerator
{
    byte[] Generate(TailoredProfile profile);
}
