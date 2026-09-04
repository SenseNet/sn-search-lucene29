# Docker image workflow

The repository-owned `docker-image.yml` workflow builds the SearchService
image from `src/SenseNet.Search.Lucene29.Centralized.GrpcService/Dockerfile`
with `src` as the Docker build context. It publishes
`sensenetcsp/sn-searchservice` to Docker Hub.

Pushes to `develop` and `master` build and publish automatically. Pull requests
targeting either branch perform build-only validation and never log in or push.
Manual runs use the branch selected in GitHub's **Run workflow** dialog and do
not publish unless `push_image` is enabled.

Published images receive the source branch tag, an immutable
`YYYYMMDD-shortSHA` tag, and the legacy TFS-compatible date tag:

- `develop.YYYY.MM.DD` on `develop`;
- `YYYY.MM.DD` on `master` or `main`.

Automatic `develop` publishes also update `preview`, while automatic `master`
publishes update `latest`. A manual publishing run may add a `custom_tag`.

The workflow requires these repository secrets for publishing:

- `DOCKERHUB_USERNAME`
- `DOCKERHUB_TOKEN`

The existing TFS implementation downloads the `Sn-Deployment` artifact only
for generic Docker build, tag, login, push, logout, and runner-cleanup scripts.
It did not provide a SearchService build asset or active preparation step, so
the GitHub workflow does not download that artifact.
