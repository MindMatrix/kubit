# kubit

```
./build.sh
docker push docker build docker -t docker-hosted.gladeos.net/dotnet-sdk8:$(date '+%Y.%m.%d.%H%M')
```

1. Requires docker-hub secret to access private repo
2. Requires git-repo-volume to store packages for performance, should be RWX
3. Should have a registry cache setup at http://registry-cache:5000 to save image pull requests
4. Requires a git mirror setup with a path for you to clone the repo
5. You will have needed to setup the git server and client keeps on the git mirror for access

Requires

1. --branch (branch to build)
2. --repo (repo to clone)
3. --file (defaults to build.yaml)
4. --build (command to pass to build project)
5. --env (can be repeated and used to set env variables for your build script)

```shell
dotnet run -- --repo git-server-service.jenkins.svc.cluster.local --branch company-usergroup --image "mindmatrix/taskmanager2" --project "Applications/MindMatrix.Applications.TaskManager2/src/taskmanager.csproj"
```


dotnet run -- --repo git-server-service.jenkins.svc.cluster.local --branch main --image "mindmatrix/taskmanager2" --project "Applications/MindMatrix.Applications.TaskManager2/src/MindMatrix.Applications.TaskManager.csproj"