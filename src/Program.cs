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

var defaultTag = DateTime.UtcNow.ToString("yyyy.MM.dd.HHmm");
var kubeconfigPath = Environment.GetEnvironmentVariable("KUBECONFIG");
Console.WriteLine("Using kubeconfig: " + kubeconfigPath);

KubernetesClientConfiguration config;
if (string.IsNullOrEmpty(kubeconfigPath))
{
    // Explicitly use the in-cluster configuration if KUBECONFIG is not set
    config = KubernetesClientConfiguration.InClusterConfig();
}
else
{
    // Use the specified kubeconfig file
    if (!File.Exists(kubeconfigPath))
    {
        throw new FileNotFoundException($"Specified kubeconfig file not found: {kubeconfigPath}");
    }

    var kubeconfigFileInfo = new FileInfo(kubeconfigPath);
    var test = await KubernetesClientConfiguration.LoadKubeConfigAsync(kubeconfigFileInfo, useRelativePaths: false);
    Console.WriteLine($"this is something: {test.FileName}");
    using var sr = new StreamReader(kubeconfigFileInfo.OpenRead());
    Console.WriteLine($"this is something2: {await sr.ReadToEndAsync()}");

    config = KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeconfigFileInfo);
}

var _kubernetesClient = new Kubernetes(config);

var envOption = new Option<string[]>("--env", "Environment variables in the format KEY=VALUE")
{
    AllowMultipleArgumentsPerToken = true
};

var branchOption = new Option<string>("--branch", "Branch to build")
{
    IsRequired = true
};

var repoOption = new Option<string>("--repo", "MindMatrix repo to clone (ex. git-amp-ssh.default.svc.cluster.local)")
{
    IsRequired = true
};

var imageOption = new Option<string>("--image", "ex. mindmatrix/<image>")
{
    IsRequired = true
};

var tagOption = new Option<string>("--tag", $"ex. {defaultTag} (yyyy.MM.dd.HHmm)")
{
};

var projectOption = new Option<string>("--project", "ex. Applications/MindMatrix.Applications.TaskManager2/MindMatrix.Applications.TaskManager2.csproj (do not prefix with /)")
{
    IsRequired = true
};

//cd /tmp/app/Applications/MindMatrix.Applications.TaskManager2 && chmod +x build.sh && ./build.sh
//dotnet run --repo git-amp-ssh.default.svc.cluster.local --branch specification --build "cd /tmp/app/Applications/MindMatrix.Applications.TaskManager2 && chmod +x builld.sh && ./build.sh $BUILD_NUMBER"
//dotnet run -- --repo git-amp-ssh.default.svc.cluster.local --branch specification --image "mindmatrix/taskmanager2" --project "Applications/MindMatrix.Applications.TaskManager2/MindMatrix.Applications.TaskManager2.csproj"
var tag = new Option<string>("--tag", "Docker tag");

var success = false;
var rootCommand = new RootCommand("test");
rootCommand.AddOption(envOption);
rootCommand.AddOption(branchOption);
rootCommand.AddOption(repoOption);
rootCommand.AddOption(imageOption);
rootCommand.AddOption(projectOption);
rootCommand.AddOption(tagOption);

rootCommand.SetHandler(async (string[] env, string branch, string repo, string image, string project, string? tag) =>
{
    if (project.Any(ch => Path.GetInvalidPathChars().Contains(ch)))
        throw new Exception("project must be a valid file path without a leading /");

    tag ??= defaultTag;

    var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
    var client = new Kubernetes(config);
    var runner = new KubernetesJobRunner(client);

    success = await runner.RunJobAsync(branch, repo, project, image, tag, env, cancellationSource.Token);
}, envOption, branchOption, repoOption, imageOption, projectOption, tagOption);

var result = await rootCommand.InvokeAsync(args);

if (result != 0)
    return result;

return success ? 0 : -1;
