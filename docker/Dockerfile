FROM mcr.microsoft.com/dotnet/sdk:8.0

# Add Docker's official GPG key:
RUN curl -fsSL https://get.docker.com | sh

RUN apt update && apt install -y git openssh-client \
    && rm -rf /var/lib/apt/lists/* \
    && apt clean
