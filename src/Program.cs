using System.CommandLine;
using System.CommandLine.Builder;
using System.Text;
using k8s;

Console.OutputEncoding = Encoding.UTF8;

var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, key) =>
{
    key.Cancel = true;
    Console.WriteLine("Cancelling...");
    cancellationSource.Cancel();
};

var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
var _kubernetesClient = new Kubernetes(config);

var fileOption = new Option<string>("--file", "Path to the YAML file")
{
};

var envOption = new Option<string[]>("--env", "Environment variables in the format KEY=VALUE")
{
    AllowMultipleArgumentsPerToken = true
};

var branchOption = new Option<string>("--branch", "Branch to build")
{
    IsRequired = true
};

var repoOption = new Option<string>("--repo", "MindMatrix repo to clone")
{
    IsRequired = true
};

var buildOption = new Option<string>("--build", "Build command to pass to the dotnet container")
{
    IsRequired = true
};

var success = false;
var rootCommand = new RootCommand("test");
rootCommand.AddOption(fileOption);
rootCommand.AddOption(envOption);
rootCommand.AddOption(branchOption);
rootCommand.AddOption(repoOption);
rootCommand.AddOption(buildOption);

rootCommand.SetHandler(async (string file, string[] env, string branch, string repo, string build) =>
{
    file ??= "build.yaml";

    var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
    var client = new Kubernetes(config);
    var runner = new KubernetesJobRunner(client);

    success = await runner.RunJobAsync(file, branch, repo, build, env, cancellationSource.Token);
}, fileOption, envOption, branchOption, repoOption, buildOption);

var result = await rootCommand.InvokeAsync(args);

if (result != 0)
    return result;

return success ? 0 : -1;
