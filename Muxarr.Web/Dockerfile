ARG FFMPEG_VERSION=8.1
ARG MKVTOOLNIX_VERSION=103.0
ARG DOVI_TOOL_VERSION=2.3.4

# ffmpeg static build from BtbN (linked from ffmpeg.org). Debian's
# apt ffmpeg is 5.1 - too old to round-trip MP4 udta.name titles.
FROM debian:12-slim AS ffmpeg-dl
ARG FFMPEG_VERSION
ARG TARGETARCH
RUN apt-get update && apt-get install -y --no-install-recommends \
      curl ca-certificates xz-utils \
    && rm -rf /var/lib/apt/lists/*
RUN set -eux; \
    case "${TARGETARCH}" in \
        amd64) btbn_arch=linux64 ;; \
        arm64) btbn_arch=linuxarm64 ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    url="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n${FFMPEG_VERSION}-latest-${btbn_arch}-gpl-${FFMPEG_VERSION}.tar.xz"; \
    curl -fsSL "${url}" -o /tmp/ffmpeg.tar.xz; \
    mkdir -p /out; \
    tar -xJf /tmp/ffmpeg.tar.xz -C /out --strip-components=2 --wildcards '*/bin/ffmpeg' '*/bin/ffprobe'; \
    /out/ffmpeg -version

# dovi_tool static (musl) build, used only to tell Dolby Vision profile 7 FEL from MEL.
FROM debian:12-slim AS dovi-dl
ARG DOVI_TOOL_VERSION
ARG TARGETARCH
RUN apt-get update && apt-get install -y --no-install-recommends \
      curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
RUN set -eux; \
    case "${TARGETARCH}" in \
        amd64) target=x86_64-unknown-linux-musl ;; \
        arm64) target=aarch64-unknown-linux-musl ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    url="https://github.com/quietvoid/dovi_tool/releases/download/${DOVI_TOOL_VERSION}/dovi_tool-${DOVI_TOOL_VERSION}-${target}.tar.gz"; \
    curl -fsSL "${url}" -o /tmp/dovi_tool.tar.gz; \
    mkdir -p /out; \
    tar -xzf /tmp/dovi_tool.tar.gz -C /out; \
    /out/dovi_tool --version

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG MKVTOOLNIX_VERSION

# mkvtoolnix from the project's own repo; the Ubuntu one in the base image is v82 from 2024.
# The key is checked against the fingerprint published on mkvtoolnix.download/authenticity.html.
RUN apt-get update && apt-get install -y --no-install-recommends \
    gosu \
    curl \
    ca-certificates \
    gnupg \
    && . /etc/os-release \
    && curl -fsSL https://mkvtoolnix.download/gpg-pub-moritzbunkus.gpg -o /usr/share/keyrings/mkvtoolnix.gpg \
    && gpg --show-keys --with-colons /usr/share/keyrings/mkvtoolnix.gpg | grep -q '^fpr:::::::::D9199745B0545F2E8197062B0F92290A445B9007:' \
    && echo "deb [signed-by=/usr/share/keyrings/mkvtoolnix.gpg] https://mkvtoolnix.download/ubuntu/ ${VERSION_CODENAME} main" > /etc/apt/sources.list.d/mkvtoolnix.list \
    && apt-get update && apt-get install -y --no-install-recommends "mkvtoolnix=${MKVTOOLNIX_VERSION}-*" \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd -g 888 appgroup \
    && useradd -u 888 -g 888 -d /config -s /bin/false appuser \
    && usermod -G users appuser

COPY --from=ffmpeg-dl /out/ffmpeg /usr/local/bin/ffmpeg
COPY --from=ffmpeg-dl /out/ffprobe /usr/local/bin/ffprobe
COPY --from=dovi-dl /out/dovi_tool /usr/local/bin/dovi_tool

WORKDIR /app

COPY publish/ ./
COPY entrypoint.sh ./entrypoint.sh
RUN chmod +x ./entrypoint.sh

# /data stays declared: older images stored the database there and compose only
# carries that anonymous volume across a recreate while the image still declares it.
VOLUME ["/config", "/data"]

ENV ASPNETCORE_HTTP_PORTS=8183
EXPOSE 8183

ENTRYPOINT ["./entrypoint.sh", "dotnet", "Muxarr.Web.dll"]
