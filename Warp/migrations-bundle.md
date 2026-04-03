Implement database migrations for my Tubester API using an EF Core migrations bundle executed as a Kubernetes Job.

My setup:
- ASP.NET Core API
- EF Core
- PostgreSQL
- k3s cluster
- Kubernetes YAML manifests are currently stored alongside the backend repo
- CI/CD builds and pushes container images, then deploys to the cluster
- I already have API deployment YAML and want migrations to happen before the API rollout
- I do not want migration execution inside the API startup path

Your tasks:
1. Inspect the solution and identify:
   - API startup project
   - infrastructure/persistence project
   - DbContext
   - current migrations assembly
2. Modify the build so an EF migrations bundle is produced.
3. Package that bundle into a runnable container image.
4. Create a Kubernetes Job manifest file named exactly:
   - `db-migration.job.yaml`
5. Wire the Job to the same database connection source as the API, ideally via Secret/env vars.
6. Make the Job deployment-friendly:
   - `restartPolicy: Never`
   - small `backoffLimit`
   - `ttlSecondsAfterFinished`
   - clearly failing logs
7. Keep the API deployment independent from migration execution.
8. Update the GitHub Actions deployment flow so it does the following in this order:
   - build and push the API image
   - substitute the newly built image tag into `db-migration.job.yaml`
   - apply/run the migration Job
   - wait for the Job to complete successfully
   - only then update the API Deployment to the exact same image tag
9. If a previous Job with the same name would block re-apply, solve that cleanly.
10. Explain whether to use a fixed Job name + delete/recreate, or a versioned Job name per image tag, and choose the better option for this repo.

Constraints:
- Prefer production-ready simplicity over cleverness
- Do not add Helm unless absolutely necessary
- Do not add app-startup auto-migrate behavior
- Minimize duplicated config
- Reuse the existing namespace / secret conventions
- Keep manifests readable
- Do not refactor unrelated files. Only make the smallest set of changes necessary to implement this cleanly.

Please output:
- full changed Dockerfile(s)
- full `db-migration.job.yaml`
- full CI workflow YAML changes
- exact `kubectl` commands for manual run
- recommended deploy sequence
- short explanation of the design decisions