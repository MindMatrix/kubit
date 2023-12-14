#!/bin/bash

# Define parameters
BUILD_NUMBER=${3:-$(date '+%Y.%m.%d.%H%M')} # Optional parameter

BUILD_START=$(date)
BUILD_YEAR=$(date '+%Y')
echo "Year: $BUILD_YEAR"

BUILD_BRANCH=$(git rev-parse --abbrev-ref HEAD)
echo "Branch: $BUILD_BRANCH"

echo "Version: $BUILD_NUMBER"

BUILD_COMMIT=$(git rev-parse --verify HEAD)
BUILD_SHORTCOMMIT=${BUILD_COMMIT:0:6}
echo "Commit: $BUILD_COMMIT"

docker build docker -t docker-hosted.gladeos.net/kubit/build:$BUILD_NUMBER
docker push docker-hosted.gladeos.net/kubit/build:$BUILD_NUMBER
