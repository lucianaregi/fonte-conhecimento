using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Fonte.Tests.Fakes;

public sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "Fonte.Tests";
    public string ContentRootPath { get; set; } = contentRootPath;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
