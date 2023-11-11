using System.Net.WebSockets;
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

    public async Task<bool> RunJobAsync(string yamlFilePath, string branch, string repo, string buildCommand, string[] envVariables, CancellationToken cancellationToken)
    {
        var jobYaml = File.ReadAllText(yamlFilePath);
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

            // Create the job
            Console.WriteLine("Creating job");
            var job = await _client.CreateNamespacedJobAsync(jobData, jobData.Metadata.NamespaceProperty, cancellationToken: cancellationToken);

            Console.WriteLine("Waiting for running state");
            await Task.Delay(5000);
            await WaitForJobToRunAsync(jobName, jobData.Metadata.NamespaceProperty, TimeSpan.FromMinutes(1), cancellationToken);

            var podName = await GetPodName(jobData.Metadata.NamespaceProperty, jobName, cancellationToken);
            if (podName == null)
            {
                Console.WriteLine("Couldn't find pod");
                return false;
            }

            var commands = new string[] {
                $"git clone --branch {branch} --single-branch --depth 1 git@git-server-service:/tmp/{repo} /tmp/app"
                // ... add the rest of your commands here ...
            };
            if (!await ExecuteCommandsInPodAsync(jobData.Metadata.NamespaceProperty, podName, "build", commands, cancellationToken))
                return false;

            commands = new string[] {
                buildCommand
                //"cd /tmp/app/Applications/MindMatrix.Applications.TaskManager2 && chmod +x build.sh && ./build.sh"
            };
            if (!await ExecuteCommandsInPodAsync(jobData.Metadata.NamespaceProperty, podName, "build", commands, cancellationToken))
                return false;

            // Stream logs
            //await StreamLogsAsync(jobData.Metadata.NamespaceProperty, podName, jobName, "build", cancellationToken);
        }
        finally
        {
            // Delete the job
            Console.WriteLine("Deleting job");
            await _client.DeleteNamespacedJobAsync(jobName, jobData.Metadata.NamespaceProperty, propagationPolicy: "Foreground");
        }
        return true;
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
    public async Task<bool> ExecuteCommandsInPodAsync(string namespaceName, string podName, string containerName, string[] commands, CancellationToken cancellationToken)
    {
        // Wait for the pod to be running
        // Code to check the pod status goes here
        foreach (var command in commands)
        {
            Console.WriteLine(">" + command);
            var execRequest = _client.WebSocketNamespacedPodExecAsync(
                podName,
                namespaceName,
                new[] { "sh", "-c", command },
                containerName,
                stderr: true,
                stdout: true,
                cancellationToken: cancellationToken);

            using (var webSocket = await execRequest)
            {
                await ReadFromWebSocketAsync(webSocket, cancellationToken);
                if (!execRequest.IsCompletedSuccessfully)
                    return false;
            }
        }

        return true;
    }

    private async Task ReadFromWebSocketAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new ArraySegment<byte>(new byte[4096]);

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
                var output = Encoding.UTF8.GetString(buffer.Array!, 0, result.Count);
                Console.Write(output);
            }
        }
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

    private async Task StreamLogsAsync(string namespaceName, string podName, string jobName, string containerName, CancellationToken cancellationToken)
    {
        var jobCompleted = false;

        // Creating a task to monitor job completion
        var jobMonitorTask = Task.Run(async () =>
        {
            while (!jobCompleted && !cancellationToken.IsCancellationRequested)
            {
                var job = await _client.ReadNamespacedJobAsync(jobName, namespaceName, cancellationToken: cancellationToken);
                if (job.Status.CompletionTime != null)
                {
                    jobCompleted = true;
                }
                await Task.Delay(1000, cancellationToken); // Polling interval
            }
        }, cancellationToken);

        // Reading logs asynchronously from the "build" container
        var logStream = await _client.ReadNamespacedPodLogAsync(podName, namespaceName, container: containerName, follow: true, cancellationToken: cancellationToken);
        using (var streamReader = new StreamReader(logStream))
        {
            while (!jobCompleted && !cancellationToken.IsCancellationRequested)
            {
                var line = await streamReader.ReadLineAsync(cancellationToken);
                if (line != null)
                {
                    Console.WriteLine(line);
                }
            }
        }

        await jobMonitorTask; // Wait for the job monitoring task to complete
        Console.WriteLine($"Job '{jobName}' in namespace '{namespaceName}' has completed.");
    }

}
