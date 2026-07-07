# ORBIT source-built preview-service image.
#
# Builds preview thumbnails from the same ORBIT-branded Speckle fork and viewer
# patches as ./frontend/Dockerfile. The GHCR `orbit-preview` image predates the
# instancing fixes and fails to render block InstanceProxy placements whose
# geometry lives only in detached `@instanceDefinitionGeometry`.
ARG NODE_ENV=production
ARG ORBIT_PREVIEW_SOURCE_COMMIT=173d64210351c0b16a64437aaeb6e3dddf5eeada

FROM node:22-bookworm-slim@sha256:221ee67425de7a3c11ce4e81e63e50caaec82ede3a7d34599ab20e59d29a0cb5 AS fetch-upstream
ARG ORBIT_PREVIEW_SOURCE_COMMIT
RUN apt-get update -y \
  && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
  ca-certificates=20230311+deb12u1 \
  git=1:2.39.5-0+deb12u2 \
  && rm -rf /var/lib/apt/lists/*
WORKDIR /src
RUN git clone https://github.com/CheekiSkrub/speckle-server-dev.git speckle-server \
  && cd speckle-server \
  && git checkout ${ORBIT_PREVIEW_SOURCE_COMMIT} \
  && echo "Built ORBIT preview from $(git rev-parse HEAD)"

FROM node:18-bookworm-slim@sha256:408f8cbbb7b33a5bb94bdb8862795a94d2b64c2d516856824fd86c4a5594a443 AS build-stage
WORKDIR /speckle-server

ARG NODE_ENV
ENV NODE_ENV=${NODE_ENV}

COPY --from=fetch-upstream /src/speckle-server/ .

# Same ORBIT viewer patches as orbit-frontend (instancing + scaled blocks).
COPY frontend/patches/ ./patches/
RUN apt-get update -y \
  && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends patch=2.7.6-7 \
  && patch -p1 < patches/viewer-displayable-lookup.patch \
  && patch -p1 < patches/viewer-instanced-line-transform.patch \
  && patch -p1 < patches/viewer-instance-definition-exclusion.patch \
  && apt-get remove -y patch \
  && apt-get autoremove -y \
  && apt-get clean \
  && rm -rf /var/lib/apt/lists/*

RUN PUPPETEER_SKIP_DOWNLOAD=true PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 yarn workspaces focus -A
RUN yarn workspaces foreach -W run build

FROM --platform=linux/amd64 node:22-bookworm-slim@sha256:221ee67425de7a3c11ce4e81e63e50caaec82ede3a7d34599ab20e59d29a0cb5 AS node

SHELL ["/bin/bash", "-o", "pipefail", "-c"]

RUN apt-get update && \
  DEBIAN_FRONTEND=noninteractive apt-get install -y \
  --no-install-recommends \
  ca-certificates=20230311+deb12u1 \
  tini=0.19.0-1+b3 \
  fonts-ipafont-gothic=00303-23 \
  fonts-wqy-zenhei=0.9.45-8 \
  fonts-thai-tlwg=1:0.7.3-1 \
  fonts-kacst=2.01+mry-15 \
  fonts-freefont-ttf=20120503-10 \
  libxss1=1:1.2.3-1 \
  libegl1=1.6.0-1 \
  libxext6=2:1.3.4-1+b1 && \
  apt-get clean && \
  rm -rf /var/lib/apt/lists/*

RUN apt-get update && \
  DEBIAN_FRONTEND=noninteractive apt-get install -y \
  gnupg=2.2.40-1.1+deb12u2 && \
  apt-get clean && \
  rm -rf /var/lib/apt/lists/*

ARG NODE_ENV
ENV NODE_ENV=${NODE_ENV}

WORKDIR /speckle-server
COPY --from=fetch-upstream /src/speckle-server/.yarnrc.yml .
COPY --from=fetch-upstream /src/speckle-server/.yarn ./.yarn
COPY --from=fetch-upstream /src/speckle-server/package.json .
COPY --from=fetch-upstream /src/speckle-server/yarn.lock .
COPY --from=fetch-upstream /src/speckle-server/packages/preview-service/package.json ./packages/preview-service/

WORKDIR /speckle-server/packages

COPY --link --from=build-stage /speckle-server/packages/shared ./shared
COPY --link --from=build-stage /speckle-server/packages/preview-service ./preview-service
COPY --link --from=build-stage /speckle-server/packages/preview-frontend/dist ./preview-service/public

WORKDIR /speckle-server/packages/preview-service

RUN PUPPETEER_SKIP_DOWNLOAD=true PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 yarn workspaces focus --production

RUN groupadd -g 30000 -r pptruser && \
  useradd -r -g pptruser -G audio,video -u 800 pptruser && \
  mkdir -p /home/pptruser/Downloads && \
  chown -R pptruser:pptruser /home/pptruser && \
  if [ -d ./node_modules ]; then chown -R pptruser:pptruser ./node_modules; else true; fi && \
  chown -R pptruser:pptruser ./package.json

ARG CACHE_BUST=1

RUN apt-get update && \
  DEBIAN_FRONTEND=noninteractive apt-get install -y \
  --no-install-recommends \
  wget && \
  wget -q -O - https://dl-ssl.google.com/linux/linux_signing_key.pub | gpg --dearmor -o /usr/share/keyrings/googlechrome-linux-keyring.gpg && \
  sh -c 'echo "deb [arch=amd64 signed-by=/usr/share/keyrings/googlechrome-linux-keyring.gpg] https://dl-ssl.google.com/linux/chrome/deb/ stable main" >> /etc/apt/sources.list.d/google.list' && \
  DEBIAN_FRONTEND=noninteractive apt-get remove -y \
  wget && \
  apt-get update && \
  DEBIAN_FRONTEND=noninteractive apt-get install -y \
  --no-install-recommends \
  google-chrome-stable && \
  apt-get clean && \
  rm -rf /var/lib/apt/lists/*

ENV CHROMIUM_EXECUTABLE_PATH="/usr/bin/google-chrome"
ENV USER_DATA_DIR='/tmp/puppeteer'

USER pptruser
ENTRYPOINT [ "tini", "--", "node", "--loader=./dist/bootstrap.js", "./dist/main.js" ]
