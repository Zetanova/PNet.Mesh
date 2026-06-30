#!/usr/bin/env bash
# Update matching NuGet PackageReference entries across project files.
# opt-status: check-required (solution-aware NuGet lookup fallback added)
# opt-date: 2026-06-22
# forks: setup=0, scan=1, outdated=1/solution-or-project, search=fallback 1/package, update=1/reference, restore=1/target
# template-id: agents-dotnet-packages-helper.v1

set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/packages.sh [--name 'PNet*'] [options]

Updates existing PackageReference entries whose package ID matches --name.
The --name value is a shell-style package ID glob; quote it to avoid shell expansion.
If --name is omitted, all PackageReference entries are considered.
By default, the latest stable version is selected. Use --prerelease to include CI/prerelease versions.

Options:
  --name PATTERN       Package ID glob, for example 'PNet*' or 'PNet.Core*'. Default: '*'.
  --exclude PATTERN    Exclude package ID glob. Can be repeated, for example 'PNet.Data*'.
  --version VERSION    Exact version to apply. If omitted, latest version is used per package.
  --pre, --prerelease  Include prerelease versions when looking up target package versions.
  --allow-downgrade    Allow --version to replace a newer current version with an older target.
  --configfile PATH    NuGet config file used for update checks and restore.
                       Default: use nuget.config when present; otherwise use NuGet defaults.
  --source SOURCE      Optional NuGet source name or URL for update checks.
  --project PATH       Limit to one project file. Can be repeated.
  --dry-run            Show changes without editing project files.
  --restore            Run dotnet restore after editing. Defaults to the single repo .sln.
  --restore-target PATH
                       Explicit project or solution to restore. Can be repeated.
  --timeout SECONDS    Timeout for each dotnet/NuGet command. Default: 60.
  -h, --help           Show this help.
USAGE
}

die() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || die "required command not found: $1"
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

name_pattern='*'
target_version=''
config_file='nuget.config'
config_file_explicit=false
source=''
dry_run=false
restore_after=false
allow_downgrade=false
include_prerelease=false
timeout_seconds=60
projects=()
exclude_patterns=()
restore_targets=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --name)
      [[ $# -ge 2 ]] || die "--name requires a value"
      name_pattern="$2"
      shift 2
      ;;
    --exclude)
      [[ $# -ge 2 ]] || die "--exclude requires a value"
      exclude_patterns+=("$2")
      shift 2
      ;;
    --version)
      [[ $# -ge 2 ]] || die "--version requires a value"
      target_version="$2"
      shift 2
      ;;
    --pre|--prerelease)
      include_prerelease=true
      shift
      ;;
    --configfile)
      [[ $# -ge 2 ]] || die "--configfile requires a value"
      config_file="$2"
      config_file_explicit=true
      shift 2
      ;;
    --source)
      [[ $# -ge 2 ]] || die "--source requires a value"
      source="$2"
      shift 2
      ;;
    --project)
      [[ $# -ge 2 ]] || die "--project requires a value"
      projects+=("$2")
      shift 2
      ;;
    --restore-target)
      [[ $# -ge 2 ]] || die "--restore-target requires a value"
      restore_targets+=("$2")
      shift 2
      ;;
    --dry-run)
      dry_run=true
      shift
      ;;
    --allow-downgrade)
      allow_downgrade=true
      shift
      ;;
    --restore)
      restore_after=true
      shift
      ;;
    --timeout)
      [[ $# -ge 2 ]] || die "--timeout requires a value"
      [[ "$2" =~ ^[0-9]+$ ]] || die "--timeout must be a positive integer"
      timeout_seconds="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      die "unknown argument: $1"
      ;;
  esac
done

config_args=()
if [[ -f "$config_file" ]]; then
  config_args=(--configfile "$config_file")
elif [[ "$config_file_explicit" == true ]]; then
  die "NuGet config file not found: $config_file"
fi

require_command dotnet
require_command timeout
require_command sort
require_command awk
require_command tail

if [[ -z "$target_version" ]]; then
  require_command jq
fi

discover_projects() {
  if [[ ${#projects[@]} -gt 0 ]]; then
    printf '%s\n' "${projects[@]}"
    return
  fi

  if command -v rg >/dev/null 2>&1; then
    rg --files -g '*.csproj' -g '!**/bin/**' -g '!**/obj/**' | sort
  else
    find . -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' |
      sed 's#^\./##' |
      sort
  fi
}

discover_solutions() {
  if command -v rg >/dev/null 2>&1; then
    rg --files -g '*.sln' -g '*.slnx' -g '!**/bin/**' -g '!**/obj/**' | sort
  else
    find . \( -name '*.sln' -o -name '*.slnx' \) -not -path '*/bin/*' -not -path '*/obj/*' |
      sed 's#^\./##' |
      sort
  fi
}

project_packages() {
  local project="$1"
  awk '
    function emit_package(line) {
      while (match(line, /Include[[:space:]]*=[[:space:]]*"[^"]+"/)) {
        package = substr(line, RSTART, RLENGTH)
        sub(/^Include[[:space:]]*=[[:space:]]*"/, "", package)
        sub(/"$/, "", package)
        print package
        line = substr(line, RSTART + RLENGTH)
      }
    }
    /<PackageReference/ {
      line = $0
      while (line !~ />/ && getline next_line > 0) {
        line = line " " next_line
      }
      emit_package(line)
    }
  ' "$project" | sort -u
}

is_excluded() {
  local package="$1"
  local pattern

  for pattern in "${exclude_patterns[@]}"; do
    # shellcheck disable=SC2053 # Intentional package-id glob matching.
    if [[ "$package" == $pattern ]]; then
      return 0
    fi
  done

  return 1
}

package_version() {
  local project="$1"
  local package="$2"

  awk -v package="$package" '
    function attr_value(line, name, value) {
      if (match(line, name "[[:space:]]*=[[:space:]]*\"[^\"]+\"")) {
        value = substr(line, RSTART, RLENGTH)
        sub("^" name "[[:space:]]*=[[:space:]]*\"", "", value)
        sub(/"$/, "", value)
        return value
      }
      return ""
    }
    /<PackageReference/ {
      line = $0
      while (line !~ />/ && getline next_line > 0) {
        line = line " " next_line
      }
      if (attr_value(line, "Include") == package) {
        print attr_value(line, "Version")
        exit
      }
    }
  ' "$project"
}

version_is_newer() {
  local target="$1"
  local current="$2"

  [[ -z "$current" ]] && return 0
  [[ "$target" != "$current" ]] || return 1

  [[ "$(printf '%s\n%s\n' "$current" "$target" | sort -V | tail -n 1)" == "$target" ]]
}

declare -A latest_versions=()
declare -A outdated_versions=()
outdated_versions_loaded=false
outdated_versions_available=false

normalize_project_path() {
  local path="$1"

  if [[ "$path" == "$repo_root/"* ]]; then
    path="${path#"$repo_root"/}"
  fi
  path="${path#./}"

  printf '%s\n' "$path"
}

record_outdated_version() {
  local project="$1"
  local package="$2"
  local version="$3"
  local key="$project|$package"

  [[ -n "$project" && -n "$package" && -n "$version" ]] || return

  if [[ -z "${outdated_versions[$key]+set}" ]] || version_is_newer "$version" "${outdated_versions[$key]}"; then
    outdated_versions[$key]="$version"
  fi
}

load_outdated_versions() {
  local targets=()
  local target output rows project package version
  local list_args

  [[ "$outdated_versions_loaded" == false ]] || return
  outdated_versions_loaded=true

  [[ -z "$target_version" ]] || return

  if [[ ${#projects[@]} -gt 0 ]]; then
    targets=("${discovered_projects[@]}")
  else
    mapfile -t solutions < <(discover_solutions)
    if [[ ${#solutions[@]} -eq 1 ]]; then
      targets=("${solutions[0]}")
    else
      targets=("${discovered_projects[@]}")
    fi
  fi

  for target in "${targets[@]}"; do
    list_args=(package list --project "$target" --outdated --format json --output-version 1)

    if [[ "$include_prerelease" == true ]]; then
      list_args+=(--include-prerelease)
    fi

    if [[ -n "$source" ]]; then
      list_args+=(--source "$source")
    fi

    list_args+=("${config_args[@]}")

    if ! output="$(timeout "${timeout_seconds}s" dotnet "${list_args[@]}")"; then
      continue
    fi

    if ! rows="$(
      printf '%s\n' "$output" |
        jq -r '
          .projects[]? as $project
          | $project.frameworks[]?.topLevelPackages[]?
          | select(.latestVersion? and .latestVersion != "" and .latestVersion != "Not found at the sources")
          | [$project.path, .id, .latestVersion]
          | @tsv
        '
    )"; then
      continue
    fi

    outdated_versions_available=true

    while IFS=$'\t' read -r project package version; do
      project="$(normalize_project_path "$project")"
      record_outdated_version "$project" "$package" "$version"
    done <<< "$rows"
  done
}

latest_version() {
  local package="$1"
  local output latest
  local search_args=(package search "$package" --exact-match --format json --verbosity minimal)

  if [[ -n "${latest_versions[$package]+set}" ]]; then
    printf '%s\n' "${latest_versions[$package]}"
    return
  fi

  if [[ -n "$source" ]]; then
    search_args+=(--source "$source")
  fi

  search_args+=("${config_args[@]}")

  if [[ "$include_prerelease" == true ]]; then
    search_args+=(--prerelease)
  fi

  if ! output="$(timeout "${timeout_seconds}s" dotnet "${search_args[@]}")"; then
    die "NuGet search failed for $package"
  fi

  latest="$(
    printf '%s\n' "$output" |
      jq -r --arg id "$package" '.searchResult[]?.packages[]? | select(.id == $id) | .version' |
      sort -V |
      tail -n 1
  )"

  latest_versions[$package]="$latest"
  printf '%s\n' "$latest"
}

target_package_version() {
  local project="$1"
  local package="$2"
  local current="$3"
  local key

  if [[ -n "$target_version" ]]; then
    printf '%s\n' "$target_version"
    return
  fi

  load_outdated_versions

  if [[ "$outdated_versions_available" == true ]]; then
    key="$(normalize_project_path "$project")|$package"
    if [[ -n "${outdated_versions[$key]+set}" ]]; then
      printf '%s\n' "${outdated_versions[$key]}"
    else
      printf '%s\n' "$current"
    fi
    return
  fi

  latest_version "$package"
}

mapfile -t discovered_projects < <(discover_projects)
[[ ${#discovered_projects[@]} -gt 0 ]] || die "no project files found"

if [[ -z "$target_version" ]]; then
  load_outdated_versions
fi

updated_count=0
matched_count=0
declare -A changed_projects=()

for project in "${discovered_projects[@]}"; do
  [[ -f "$project" ]] || die "project file not found: $project"

  while IFS= read -r package; do
    [[ -n "$package" ]] || continue
    # shellcheck disable=SC2053 # --name intentionally uses a shell-style package glob.
    [[ "$package" == $name_pattern ]] || continue
    if is_excluded "$package"; then
      continue
    fi

    matched_count=$((matched_count + 1))
    current="$(package_version "$project" "$package")"
    target="$(target_package_version "$project" "$package" "$current")"

    if [[ -z "$target" ]]; then
      if [[ "$include_prerelease" == true ]]; then
        printf 'skip   %s %s no stable/prerelease version found\n' "$project" "$package"
      else
        printf 'skip   %s %s no stable version found\n' "$project" "$package"
      fi
      continue
    fi

    if [[ "$current" == "$target" ]]; then
      printf 'skip   %s %s already %s\n' "$project" "$package" "$target"
      continue
    fi

    if [[ "$allow_downgrade" != true ]] && ! version_is_newer "$target" "$current"; then
      printf 'skip   %s %s current %s >= target %s\n' "$project" "$package" "$current" "$target"
      continue
    fi

    if [[ -z "$current" ]]; then
      current='(no inline version)'
    fi

    if [[ "$dry_run" == true ]]; then
      printf 'would  %s %s %s -> %s\n' "$project" "$package" "$current" "$target"
      continue
    fi

    printf 'update %s %s %s -> %s\n' "$project" "$package" "$current" "$target"
    timeout "${timeout_seconds}s" dotnet add "$project" package "$package" --version "$target" --no-restore >/dev/null
    updated_count=$((updated_count + 1))
    changed_projects["$project"]=1
  done < <(project_packages "$project")
done

if [[ "$matched_count" -eq 0 ]]; then
  printf 'No PackageReference entries matched %s\n' "$name_pattern"
  exit 0
fi

if [[ "$dry_run" == true ]]; then
  printf 'Dry run complete. Re-run without --dry-run to apply updates.\n'
  exit 0
fi

printf 'Updated %d package reference(s).\n' "$updated_count"

if [[ "$restore_after" == true ]]; then
  if [[ ${#restore_targets[@]} -eq 0 ]]; then
    if [[ ${#projects[@]} -gt 0 ]]; then
      restore_targets=("${projects[@]}")
    else
      mapfile -t solutions < <(discover_solutions)
      if [[ ${#solutions[@]} -eq 1 ]]; then
        restore_targets=("${solutions[0]}")
      elif [[ ${#solutions[@]} -gt 1 ]]; then
        die "multiple solution files found; pass --restore-target"
      elif [[ "$updated_count" -gt 0 ]]; then
        restore_targets=("${!changed_projects[@]}")
      else
        die "no restore target found; pass --restore-target"
      fi
    fi
  fi

  for restore_target in "${restore_targets[@]}"; do
    printf 'restore %s\n' "$restore_target"
    restore_args=(restore "$restore_target" --verbosity minimal)
    restore_args+=("${config_args[@]}")
    timeout "${timeout_seconds}s" dotnet "${restore_args[@]}"
  done
fi

# --- Testing ---
# Inputs: run from the repository root or any subdirectory through scripts/packages.sh.
# Key functions: project discovery, multiline PackageReference parsing, package ID filtering, project-aware outdated package lookup, dotnet PackageReference update, solution restore verification.
# Edge cases: quote glob patterns such as 'PNet*'; use --prerelease for CI packages; exact --version skips update lookup and downgrades unless --allow-downgrade is set; default nuget.config is optional but explicit --configfile must exist.
# Run instructions: bash -n scripts/packages.sh && scripts/test/packages-configfile.sh && scripts/test/packages-outdated-list.sh
