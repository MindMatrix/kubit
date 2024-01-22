FROM mcr.microsoft.com/dotnet/sdk:8.0

# Add Docker's official GPG key:
RUN curl -fsSL https://get.docker.com | sh

RUN apt update && apt install -y git openssh-client \
    && rm -rf /var/lib/apt/lists/* \
    && apt clean

RUN curl -LO "https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl"
RUN install -o root -g root -m 0755 kubectl /usr/local/bin/kubectl


# FROM jenkins/inbound-agent:latest

# USER root

# RUN curl -fsSL https://get.docker.com | sh

# # Install .NET SDK
# RUN apt-get update && \
#     apt-get install -y apt-transport-https software-properties-common wget git openssh-client && \
#     wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
#     dpkg -i packages-microsoft-prod.deb && \
#     rm packages-microsoft-prod.deb && \
#     apt-get update && \
#     apt-get install -y dotnet-sdk-8.0 && \
#     rm -rf /var/lib/apt/lists/* && apt clean

# Switch back to the jenkins user
#USER jenkins