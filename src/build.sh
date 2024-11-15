#!/bin/bash

# Check if required parameters are provided
if [ -z "$1" ] || [ -z "$2" ] || [ -z "$3" ]; then
    echo "Usage: $0 <csproj_path> <docker_image_name> <tag>"
    exit 1
fi

# Define parameters
CS_PROJ_PATH=$1
BUILD_IMAGE=$2
BUILD_TAG=$3
DOCKER_URL=docker-hosted.gladeos.net

BUILD_BRANCH=$(git rev-parse --abbrev-ref HEAD)
echo "Branch: $BUILD_BRANCH"

echo "Image: $BUILD_IMAGE:$BUILD_TAG"

BUILD_COMMIT=$(git rev-parse --verify HEAD)
BUILD_SHORTCOMMIT=${BUILD_COMMIT:0:6}
echo "Commit: $BUILD_COMMIT"

dotnet tool restore
dotnet nuke specification

dotnet publish $CS_PROJ_PATH --os linux --arch x64 -c Debug -p:ContainerImageTag=$BUILD_TAG
if [ $? -ne 0 ]; then
    echo "Build failed."
    exit 1
fi


# Check if Docker credentials are set
if [[ -n $DOCKER_USERNAME && -n $DOCKER_PAT ]]; then
    echo "Docker credentials found, performing docker login."
    echo "$DOCKER_PAT" | docker login "$DOCKER_URL" -u "$DOCKER_USERNAME" --password-stdin
    if [ $? -ne 0 ]; then
        echo "Docker login failed."
        exit 1
    fi

    echo "Pushing Docker image..."
    docker push $BUILD_IMAGE:$BUILD_TAG
    if [ $? -ne 0 ]; then
        echo "Docker push failed."
        docker logout
        exit 1
    fi
    
    docker logout "$DOCKER_URL"
else
    echo "Docker credentials not found, skipping docker login and push."
fi
