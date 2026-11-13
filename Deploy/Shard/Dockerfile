# The Britannia Reborn shard: the Linux headless server build (ServerBuilder) in a small Ubuntu image.
# Build context: a folder holding this Dockerfile and the build in server/ (see README.md; the workflow makes it).
FROM ubuntu:22.04

# A Unity Linux dedicated-server player needs only glibc and friends from the base image; UnityWebRequest (the website
# login check) needs the CA roots for HTTPS.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates tzdata \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home --uid 10001 shard

COPY server/ /srv/shard/
RUN chmod +x /srv/shard/UOGameServer.x86_64 && chown -R shard:shard /srv/shard

# The baked world (Azure Files share, read-only) and the saves (Azure Files share). ACI mounts both world-writable.
ENV BR_WORLD_DIR=/mnt/world \
    BR_DATA_DIR=/mnt/data
# BR_ACCOUNT_API and BR_SHARD_KEY come from the container group (shard.bicep).

USER shard
WORKDIR /srv/shard
EXPOSE 7770/udp
ENTRYPOINT ["./UOGameServer.x86_64", "-batchmode", "-nographics", "-logFile", "-", "-server", "-port", "7770"]
