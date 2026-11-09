#!/bin/sh
# Branch housekeeping: a branch that is already merged into origin/main has done its job.
#
#   scripts/branches.sh                  list what is safe to delete
#   scripts/branches.sh --delete         delete those branches, locally and on the remote
#   scripts/branches.sh --delete --yes   same, without the confirmation line
#
# The remote half can also be automatic: GitHub → Settings → General → "Automatically delete head
# branches". That only covers branches whose pull request merges after it is switched on; this
# script is for everything older, and for the local copies.
#
# Deleting a remote branch needs a token with contents:write. Export GITHUB_TOKEN (the same one
# pushes use) or have a credential helper configured.
set -eu

root=$(git rev-parse --show-toplevel)
cd "$root"

mode=plan
yes=0
for arg in "$@"; do
  case "$arg" in
    --delete) mode=delete ;;
    --yes) yes=1 ;;
    -h | --help)
      sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *)
      echo "unknown option: $arg" >&2
      exit 2
      ;;
  esac
done

git fetch --prune --quiet origin
main=$(git rev-parse --verify origin/main)
current=$(git branch --show-current || true)

local_merged=""
for branch in $(git for-each-ref --format='%(refname:short)' refs/heads); do
  [ "$branch" = "main" ] && continue
  [ "$branch" = "$current" ] && continue
  if git merge-base --is-ancestor "refs/heads/$branch" "$main"; then
    local_merged="$local_merged $branch"
  fi
done

remote_merged=""
for ref in $(git for-each-ref --format='%(refname:short)' refs/remotes/origin); do
  case "$ref" in origin | origin/HEAD) continue ;; esac
  branch=${ref#origin/}
  [ "$branch" = "main" ] && continue
  if git merge-base --is-ancestor "$ref" "$main"; then
    remote_merged="$remote_merged $branch"
  fi
done

if [ -z "${local_merged# }" ] && [ -z "${remote_merged# }" ]; then
  echo "nothing to do: every branch is either main or unmerged"
  exit 0
fi

echo "main is $(git rev-parse --short "$main")"
[ -n "${local_merged# }" ] && echo "local, merged:${local_merged}"
[ -n "${remote_merged# }" ] && echo "remote, merged:${remote_merged}"

if [ "$mode" = plan ]; then
  echo
  echo "dry run — re-run with --delete to remove them"
  exit 0
fi

if [ "$yes" -eq 0 ]; then
  printf 'delete these branches? [y/N] '
  read -r answer
  case "$answer" in
    y | Y | yes | YES) ;;
    *) echo "aborted"; exit 1 ;;
  esac
fi

for branch in ${local_merged# }; do
  git branch -D "$branch" >/dev/null && echo "deleted local $branch"
done

helper='!f() { echo "username=x-access-token"; echo "password=$GITHUB_TOKEN"; }; f'
for branch in ${remote_merged# }; do
  if git -c credential.helper="$helper" push --quiet origin --delete "$branch"; then
    echo "deleted remote $branch"
  else
    echo "could not delete remote $branch (needs contents:write)" >&2
  fi
done
