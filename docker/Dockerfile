# syntax=docker/dockerfile:1
# Multi-stage build: web UI (Node), backend (.NET 10 SDK), then a slim ASP.NET runtime image.
# Build from the repository root:  docker build -f docker/Dockerfile -t requestarr:dev .
# The web UI and backend are built on the build machine's own platform and the backend is published for the target
# (linux-x64 or linux-arm64), so a multi-platform build (.github/workflows/image.yml) only emulates the last stage.
# Nothing secret goes in: the build context is the committed files only (secrets.env is never committed). The one
# deliberate exception is Requestarr's own key for The Movie Database, below, which the owner chose to ship.

FROM --platform=$BUILDPLATFORM node:20-bookworm-slim AS frontend
WORKDIR /src
COPY package.json yarn.lock .yarnrc tsconfig.json ./
RUN yarn install --frozen-lockfile --network-timeout 120000
COPY frontend/ frontend/
RUN yarn build --env production

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG TARGETARCH
# The build's number, stamped here and nowhere in the source (docs/development.md#versions): 0.7.0004 for a build sent
# to testers, the next number for dev and QA builds. Without one the build is 0.0.0.
ARG BUILD_NUMBER=0.0.0
WORKDIR /src
COPY .editorconfig LICENSE CHANGELOG.md QA-CHECKLIST.md ./
COPY changelog/ changelog/
COPY src/ src/
# global.json pins the local SDK patch; the image's 10.0 SDK is used as-is.
RUN case "$TARGETARCH" in arm64) RID=linux-arm64 ;; *) RID=linux-x64 ;; esac \
 && for project in src/NzbDrone.Console/Requestarr.Console.csproj src/NzbDrone.Mono/Requestarr.Mono.csproj; do \
      dotnet publish "$project" -c Release -f net10.0 -r "$RID" --self-contained false -p:Platform=Posix -p:SolutionDir=/src/src/ -p:RequestarrVersion="$BUILD_NUMBER" -o /app/bin || exit 1; \
    done
# Requestarr's own key for The Movie Database (cast on results and cards), from the build secret tmdb_key: the Image
# workflow passes the repository secret TMDB_API_KEY. A build without it (a fork's, the local stacks') has no built-in
# key. A secret mount, never a build argument, so the key isn't in the image's history or the build log. An admin's
# own key in REQUESTARR_TMDB_API_KEY wins over it (TmdbSettingsStore).
# KEY_STAMP changes every workflow run, so this step always runs again: a secret's contents aren't part of the build
# cache's key, and a cached layer would keep an old key after it's rotated.
ARG KEY_STAMP=
RUN --mount=type=secret,id=tmdb_key \
    : "${KEY_STAMP}" && \
    if [ -s /run/secrets/tmdb_key ]; then tr -d '\r\n' < /run/secrets/tmdb_key > /app/bin/the-movie-database-key.txt; fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /config \
 && chown app:app /config
COPY --from=backend /app/bin /app/bin
COPY --from=frontend /src/_output/UI /app/bin/UI
COPY LICENSE /app/LICENSE

# Updates come from new images, never from inside the container.
ENV REQUESTARR__UPDATE__MECHANISM=Docker

# How the build shows its version: its number (0.7.0004), or for a dev or QA build the next number and its commit
# (0.7.0004-dev.4f2a9c1). Set by the Image workflow and the local stacks' scripts.
ARG RELEASE_VERSION=
ENV REQUESTARR_RELEASE_VERSION=$RELEASE_VERSION

USER app
VOLUME /config
EXPOSE 9797
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
  CMD curl -fsS http://localhost:9797/ping || exit 1
ENTRYPOINT ["/app/bin/Requestarr", "-nobrowser", "-data=/config"]
