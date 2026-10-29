# Dedicated server image for Edgegap (plan section 4). Setup → "Build Linux server" copies this file next to the
# build in Builds/LinuxServer. From that folder:
#   docker build -t airwing-server:<version> .
#   docker push <your Edgegap registry>/airwing-server:<version>
# or from the repo root: ./Tools/Edgegap/push.ps1 -Registry registry.edgegap.com/<org>/<app> -Version <version>
# In the Edgegap app version, map the game port 7979/UDP (ChopperBootstrap.LocalPort; "-port" overrides it).
FROM ubuntu:22.04

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY . /app
RUN chmod +x /app/AirwingServer.x86_64

EXPOSE 7979/udp
ENTRYPOINT ["/app/AirwingServer.x86_64", "-batchmode", "-nographics", "-logFile", "-", "-port", "7979"]
