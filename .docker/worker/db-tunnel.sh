#!/bin/sh
#db-tunnel.sh
set -eu

mkdir -p /root/.ssh
chmod 700 /root/.ssh

cp /mounted-key/id_ed25519 /root/.ssh/id_ed25519
chmod 600 /root/.ssh/id_ed25519

exec autossh \
  -M 0 \
  -N \
  -o StrictHostKeyChecking=no \
  -o ExitOnForwardFailure=yes \
  -o ServerAliveInterval=30 \
  -o ServerAliveCountMax=3 \
  -L 0.0.0.0:5432:127.0.0.1:5432 \
  "${SSH_REMOTE_USER}@${SSH_REMOTE_HOST}" \
  -p "${SSH_REMOTE_PORT:-22}" \
  -i /root/.ssh/id_ed25519