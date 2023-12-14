pipeline {
    agent any
    
    environment {        
        BRANCH = "${BRANCH_NAME}"
        TASK = "${TASK_NAME}"
        SAFEBRANCH = "${env.BRANCH.find(/[a-zA-Z0-9\-\.]+/)}"
        VERSION = "${(new Date()).format("yyyy.MM.dd")}.${BUILD_ID}"
    }
    stages {
        stage("setup") {
            steps {    
                script {
                    currentBuild.displayName = "${VERSION}+${SAFEBRANCH}"
                    echo 'Building version ' + VersionNumber([versionNumberString : "${VERSION}-${SAFEBRANCH}", projectStartDate : '2017-01-01'])
                }
            }
        }
        stage("build"){
            steps{
                sh 'git clean -xfd'
                // withKubeCredentials([
                //     [credentialsId: 'kubeconfig'],
                // ]) {
                sh 'cd src && dotnet run -- --repo git-amp-ssh.default.svc.cluster.local --branch "$BRANCH" --tag "$VERSION-$SAFEBRANCH" --image "docker-hosted.gladeos.net/amp/taskmanager" --project "Applications/MindMatrix.Applications.TaskManager2/src/MindMatrix.Applications.TaskManager.csproj"'
                //}  
            }
        }
        // stage("deploy"){
        //     steps {
        //         withKubeCredentials([
        //             [credentialsId: 'kubeconfig'],
        //         ]) {
        //             sh 'kubectl set image -n ampdevelop "deployment/task-$TASK" tm=mindmatrix/taskmanager2:$VERSION-$SAFEBRANCH'
        //         }                
        //     }
        // } 
    }
}

