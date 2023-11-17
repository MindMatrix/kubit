using System.Net.WebSockets;
using System.Reflection.Metadata;
using System.Text;
using k8s;
using k8s.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class KubernetesJobRunner
{
    private readonly Kubernetes _client;

    public KubernetesJobRunner(Kubernetes client)
    {
        _client = client;
    }

    public async Task<bool> RunJobAsync(string branch, string repo, string project, string image, string tag, string[] envVariables, CancellationToken cancellationToken)
    {
        var jobYaml = File.ReadAllText("build.yaml");
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeInspector(inner => new JsonPropertyNameTypeInspector(inner))
            .Build();
        var jobData = deserializer.Deserialize<V1Job>(jobYaml);
        var jobName = "build-" + DateTime.UtcNow.Ticks;
        jobData.Metadata.Name = jobName;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Inject environment variables
            InjectEnvironmentVariables(jobData, envVariables);

            var createdPvc = await EnsurePersistentVolumeClaimAsync(jobData.Metadata.NamespaceProperty, "git-repo", cancellationToken);

            // Create the job
            Console.WriteLine("Creating job");
            var job = await _client.CreateNamespacedJobAsync(jobData, jobData.Metadata.NamespaceProperty, cancellationToken: cancellationToken);

            Console.WriteLine("Waiting for running state");
            await Task.Delay(5000, cancellationToken);
            await WaitForJobToRunAsync(jobName, jobData.Metadata.NamespaceProperty, TimeSpan.FromMinutes(5), cancellationToken);

            var podName = await GetPodName(jobData.Metadata.NamespaceProperty, jobName, cancellationToken);
            if (podName == null)
            {
                Console.WriteLine("Couldn't find pod");
                return false;
            }

            var commands = new string[] {
                $"ssh-keyscan {repo} > ~/.ssh/known_hosts"
            };

            if (!await ExecuteCommandsInPodAsync(jobData.Metadata.NamespaceProperty, podName, "build", commands, cancellationToken))
                return false;

            if (createdPvc)
            {
                var pvcCommands = new string[] {
                    $"git clone --branch main git@{repo}:/tmp/repo /mnt/data/app"
                };

                if (!await ExecuteCommandsInPodAsync(jobData.Metadata.NamespaceProperty, podName, "build", pvcCommands, cancellationToken))
                    return false;
            }

            // var commands = new string[] {
            //     $"ssh-keyscan {repo} > ~/.ssh/known_hosts",
            //     $"git clone --branch {branch} --single-branch --depth 1 git@{repo}:/tmp/repo /mnt/data/app",
            //     $"cd /mnt/data/app",
            //     $"git fetch --depth 1 origin specification",
            //     $"git merge specification",
            // };
            commands = new string[] {
                $"cd /mnt/data/app",
                $"git config --global user.email \"you@example.com\"",
                $"git config --global user.name \"Your Name\"",
                $"git fetch --prune",
                $"git clean -xfd",
                $"git checkout {branch}",
                $"git pull",
                $"git merge origin/specification --no-edit --no-commit --no-ff",
            };

            if (!await ExecuteCommandsInPodAsync(jobData.Metadata.NamespaceProperty, podName, "build", commands, cancellationToken))
                return false;

            await CopyFileToPodAsync(jobData.Metadata.NamespaceProperty, podName, "build", "build.sh", "/mnt/data/app/build.sh", cancellationToken);

            commands = [
                $"cd /mnt/data/app",
                $"./build.sh '{project}' '{image}' '{tag}'"
            ];
            if (!await ExecuteCommandsInPodAsync(jobData.Metadata.NamespaceProperty, podName, "build", commands, cancellationToken))
                return false;

        }
        finally
        {
            // Delete the job
            Console.WriteLine("Deleting job");
            await _client.DeleteNamespacedJobAsync(jobName, jobData.Metadata.NamespaceProperty, propagationPolicy: "Foreground");
        }
        return true;
    }


    public async Task<bool> EnsurePersistentVolumeClaimAsync(string namespaceName, string pvcName, CancellationToken cancellationToken)
    {
        try
        {
            // Check if the PVC already exists
            var pvc = await _client.ReadNamespacedPersistentVolumeClaimAsync(pvcName, namespaceName, cancellationToken: cancellationToken);
            Console.WriteLine($"PVC '{pvcName}' already exists in namespace '{namespaceName}'.");
            return false;
        }
        catch (k8s.Autorest.HttpOperationException ke) when (ke.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // PVC not found, create a new one
            Console.WriteLine($"Creating PVC '{pvcName}' in namespace '{namespaceName}'.");
            var newPvc = new V1PersistentVolumeClaim
            {
                Metadata = new V1ObjectMeta
                {
                    Name = pvcName
                },
                Spec = new V1PersistentVolumeClaimSpec
                {

                    AccessModes = new[] { "ReadWriteOnce" },
                    Resources = new V1ResourceRequirements
                    {
                        Requests = new System.Collections.Generic.Dictionary<string, ResourceQuantity>
                        {
                            { "storage", new ResourceQuantity("30Gi") }
                        }
                    }
                }
            };

            var createdPvc = await _client.CreateNamespacedPersistentVolumeClaimAsync(newPvc, namespaceName, cancellationToken: cancellationToken);
            Console.WriteLine($"PVC '{createdPvc.Metadata.Name}' created in namespace '{namespaceName}'.");
            return true;
        }
    }

    public async Task WaitForJobToRunAsync(string jobName, string namespaceName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var podsList = await _client.ListNamespacedPodAsync(namespaceName, labelSelector: $"job-name={jobName}");
            var areAllPodsRunning = podsList.Items.All(p => p.Status.Phase == "Running");

            if (areAllPodsRunning)
            {
                Console.WriteLine("All pods are now running.");
                return;
            }

            // Wait for a short interval before checking again.
            await Task.Delay(TimeSpan.FromSeconds(1));

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Timeout waiting for job to enter running state.");
            }
        }
    }
    public async Task<bool> CopyFileToPodAsync(string namespaceName, string podName, string containerName, string localFilePath, string remoteFilePath, CancellationToken cancellationToken)
    {
        // Wait for the pod to be running
        // Code to check the pod status goes here
        Console.WriteLine($"> cp {localFilePath} {podName}[{containerName}]/{remoteFilePath}");
        var lines = File.ReadAllLines(localFilePath);
        var commandLines = new List<string>();
        foreach (var it in lines)
            commandLines.Add($"echo '{it.Replace("'", "\\'")}' >> {remoteFilePath}");

        commandLines.Add($"chmod +x {remoteFilePath}");
        return await ExecuteCommandsInPodAsync(namespaceName, podName, containerName, commandLines, cancellationToken);
    }

    private async Task<bool> ExecuteCommandsInPodAsync(string namespaceName, string podName, string containerName, IEnumerable<string> commands, CancellationToken cancellationToken)
    {
        var command = string.Join(" && ", commands);
        Console.WriteLine(">> " + command);
        var execRequest = _client.WebSocketNamespacedPodExecAsync(
            podName,
            namespaceName,
            ["sh", "-c", command],
            containerName,
            stderr: true,
            stdout: true,
            cancellationToken: cancellationToken);

        using (var webSocket = await execRequest)
        {
            var result = await ReadFromWebSocketAsync(webSocket, cancellationToken);
            if (!execRequest.IsCompletedSuccessfully || result == null)
                return false;

            //cphillips83: could probably handle this better
            if (!result.Contains("\"status\":\"Success\""))
                return false;
        }

        return true;
    }

    private async Task<string?> ReadFromWebSocketAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new ArraySegment<byte>(new byte[4096]);
        string? output = null;

        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                Console.WriteLine();
                break;
            }
            if (result.MessageType != WebSocketMessageType.Text)
            {
                output = Encoding.UTF8.GetString(buffer.Array!, 0, result.Count);
                Console.Write(output);
            }
        }
        return output;
    }


    private void InjectEnvironmentVariables(V1Job job, string[] envVariables)
    {
        var envVars = new List<V1EnvVar>();
        foreach (var env in envVariables)
        {
            var parts = env.Split('=');
            if (parts.Length == 2)
            {
                envVars.Add(new V1EnvVar(parts[0], parts[1]));
            }
        }

        // Assuming the job has only one container and its name is 'build'
        var container = job.Spec.Template.Spec.Containers.FirstOrDefault(c => c.Name == "build");
        if (container != null)
        {
            foreach (var it in envVars)
                container.Env.Add(it);
        }
    }
    private async Task<string?> GetPodName(string namespaceName, string jobName, CancellationToken cancellationToken)
    {
        var podsList = await _client.ListNamespacedPodAsync(namespaceName, labelSelector: $"job-name={jobName}", cancellationToken: cancellationToken);

        // Assuming the first pod is the one we're interested in
        var pod = podsList.Items.FirstOrDefault();
        if (pod == null)
        {
            Console.WriteLine("No pods found for the specified job.");
            return null;
        }

        return pod.Metadata.Name;
    }

    // private async Task StreamLogsAsync(string namespaceName, string podName, string jobName, string containerName, CancellationToken cancellationToken)
    // {
    //     var jobCompleted = false;

    //     // Creating a task to monitor job completion
    //     var jobMonitorTask = Task.Run(async () =>
    //     {
    //         while (!jobCompleted && !cancellationToken.IsCancellationRequested)
    //         {
    //             var job = await _client.ReadNamespacedJobAsync(jobName, namespaceName, cancellationToken: cancellationToken);
    //             if (job.Status.CompletionTime != null)
    //             {
    //                 jobCompleted = true;
    //             }
    //             await Task.Delay(1000, cancellationToken); // Polling interval
    //         }
    //     }, cancellationToken);

    //     // Reading logs asynchronously from the "build" container
    //     var logStream = await _client.ReadNamespacedPodLogAsync(podName, namespaceName, container: containerName, follow: true, cancellationToken: cancellationToken);
    //     using (var streamReader = new StreamReader(logStream))
    //     {
    //         while (!jobCompleted && !cancellationToken.IsCancellationRequested)
    //         {
    //             var line = await streamReader.ReadLineAsync(cancellationToken);
    //             if (line != null)
    //             {
    //                 Console.WriteLine(line);
    //             }
    //         }
    //     }

    //     await jobMonitorTask; // Wait for the job monitoring task to complete
    //     Console.WriteLine($"Job '{jobName}' in namespace '{namespaceName}' has completed.");
    // }

}
