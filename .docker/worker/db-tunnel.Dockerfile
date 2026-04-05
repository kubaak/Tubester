FROM debian:bookworm-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends autossh openssh-client ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY .docker/worker/db-tunnel.sh /db-tunnel.sh
RUN chmod +x /db-tunnel.sh

CMD ["/db-tunnel.sh"]