# ORBIT source-built frontend image — CUSTOM BRANDED build.
#
# Builds the REBUS/ORBIT-branded Speckle fork from source:
#   CheekiSkrub/speckle-server-dev @ branch rebus-dev (pinned commit below).
# This branch carries the ORBIT branding (orbit_logo.png, "ORBIT" wordmark,
# Version/Login panels) and the custom viewer (setup.ts, object-sidebar labels,
# geometry/material converters). Do NOT point this at the fork's `main` branch —
# that is STOCK upstream Speckle with no ORBIT branding.
ARG NODE_ENV=production
# rebus-dev branded HEAD. Bump to a newer rebus-dev commit (or a REBUS-v* tag) to update.
ARG ORBIT_FRONTEND_SOURCE_COMMIT=173d64210351c0b16a64437aaeb6e3dddf5eeada

FROM node:22-bookworm-slim@sha256:221ee67425de7a3c11ce4e81e63e50caaec82ede3a7d34599ab20e59d29a0cb5 AS fetch-upstream
ARG ORBIT_FRONTEND_SOURCE_COMMIT
RUN apt-get update -y \
  && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
  ca-certificates=20230311+deb12u1 \
  git=1:2.39.5-0+deb12u2 \
  && rm -rf /var/lib/apt/lists/*
WORKDIR /src
RUN git clone https://github.com/CheekiSkrub/speckle-server-dev.git speckle-server \
  && cd speckle-server \
  && git checkout ${ORBIT_FRONTEND_SOURCE_COMMIT} \
  && echo "Built ORBIT frontend from $(git rev-parse HEAD)"

FROM node:22-bookworm-slim@sha256:221ee67425de7a3c11ce4e81e63e50caaec82ede3a7d34599ab20e59d29a0cb5 AS build-stage
WORKDIR /speckle-server

ARG TINI_VERSION=v0.19.0
RUN apt-get update -y \
  && DEBIAN_FRONTEND=noninteractive apt-get install -y \
  --no-install-recommends \
  ca-certificates=20230311+deb12u1 \
  curl=7.88.1-10+deb12u14 \
  && curl -fsSL https://github.com/krallin/tini/releases/download/${TINI_VERSION}/tini -o /usr/bin/tini \
  && chmod +x /usr/bin/tini \
  && apt-get remove -y curl \
  && apt-get autoremove -y \
  && apt-get clean \
  && rm -rf /var/lib/apt/lists/*

COPY --from=fetch-upstream /src/speckle-server/ .

ARG NODE_ENV
ENV NODE_ENV=${NODE_ENV}
RUN PUPPETEER_SKIP_DOWNLOAD=true PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 yarn workspaces focus -A
RUN yarn workspaces foreach --exclude "@speckle/frontend-2" -W run build

ENV BUILD_SOURCEMAPS=false
ENV SKIP_LIBRARY_MINIFICATION=true
ENV HYDRATION_MISMATCH_REPORTING=false
RUN yarn workspaces foreach --include "packages/frontend-2" -W run build

FROM gcr.io/distroless/nodejs22-debian12:nonroot@sha256:ed26b3ab750110c51d9dbdfd6c697561dc40a01c296460c3494d47b550ef4126 AS production-stage
ENV PORT=8080
ENV NUXT_PUBLIC_MIXPANEL_TOKEN_ID=acd87c5a50b56df91a795e999812a3a4
ENV NUXT_PUBLIC_MIXPANEL_API_HOST=https://analytics.speckle.systems

WORKDIR /speckle-server
COPY --link --from=build-stage /speckle-server/packages/frontend-2/.output .
COPY --link --from=build-stage /usr/bin/tini /usr/bin/tini

ARG NODE_ENV
ENV NODE_ENV=${NODE_ENV}
USER nonroot
EXPOSE ${PORT}
ENTRYPOINT [ "tini", "--", "/nodejs/bin/node", "./server/index.mjs" ]
