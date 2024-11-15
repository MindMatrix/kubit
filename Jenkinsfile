pipeline {
    agent {
        kubernetes {
            yamlFile 'KubernetesPod.yaml'
        }
    }
    
    environment { 
        BUILD_IMAGE = "mindmatrix/taskmanager2"       
        BUILD_PATH = "/tmp/app"
        BUILD_BRANCH = "${BRANCH_NAME}"
        SAFEBRANCH = "${env.BUILD_BRANCH.find(/[a-zA-Z0-9\-\.]+/)}"
        BUILD_NUMBER = "${(new Date()).format("yyyy.MM.dd")}.${BUILD_ID}"
        BUILD_CONFIGURATION = "Debug"
        BUILD_TAG = "${BUILD_NUMBER}-${SAFEBRANCH}"
    }
    stages {
        stage("setup") {
            steps {    
                script {
                    currentBuild.displayName = "${BUILD_NUMBER}+${SAFEBRANCH}"
                    echo 'Building version ' + VersionNumber([versionNumberString : "${BUILD_TAG}", projectStartDate : '2017-01-01'])
                }
            }
        }
        stage("clone"){
            steps{
                container('dotnet-sdk8') {
                    sh 'mkdir -p ~/.ssh'
                    sh 'cp /tmp/ssh/id_rsa ~/.ssh/id_rsa '
                    sh 'chmod 600 ~/.ssh/id_rsa '
                    sh 'ssh-keyscan git-amp-ssh.jenkins.svc.cluster.local > ~/.ssh/known_hosts'
                    sh 'git clone --depth 1 --branch $BUILD_BRANCH git@git-amp-ssh.jenkins.svc.cluster.local:/tmp/repo /tmp/app'
                }
            }
        }
        stage("build"){
            steps{
                container('dotnet-sdk8') {
                    sh 'dotnet tool restore --tool-manifest "$BUILD_PATH/.config/dotnet-tools.json"'
                    sh 'dotnet nuke specification --root "$BUILD_PATH"'
                    sh 'dotnet publish "$BUILD_PATH/Applications/MindMatrix.Applications.TaskManager2/src/MindMatrix.Applications.TaskManager.csproj" --os linux --arch x64 -c $BUILD_CONFIGURATION -p:ContainerImageTag=$BUILD_TAG -p:ContainerRepository=$BUILD_IMAGE'
                }
            }
        }        
        stage("docker"){
            steps{
                container('dotnet-sdk8') {
                    withCredentials([usernamePassword(credentialsId: 'dockerio', usernameVariable: 'DOCKER_USERNAME', passwordVariable: 'DOCKER_PAT')]) {
                        script{
                            try {
                                sh 'docker login -u $DOCKER_USERNAME -p $DOCKER_PAT'
                                sh 'docker push $BUILD_IMAGE:$BUILD_TAG'
                            } catch (Exception e) {
                                echo "An error occurred: ${e.getMessage()}"
                            } finally {
                                sh 'docker logout'
                            }
                        }
                    }
                }
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

