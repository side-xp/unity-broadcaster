# Contributing

First of all, thank you for considering contributing to a *Sideways Experiments* project!

At Sideways Experiments, we hate doing the same thing more than once, and we love to share knowledge. Whether because you "just want to help" or share that generous laziness philosophy, you're welcome!

## Developer setup

This package is developed as part of an internal Unity project, where it (and our other packages) live under `Packages/` as Git submodules. You don't need that project to contribute, though: the package is self-contained and can be developed inside any Unity project.

### Requirements

- **Unity**: minimum supported version is **2021.3**
- **.NET SDK 8.0+**: only required if you want to (re)generate the API documentation locally (see [Documentation](#documentation) below).
- **Python 3 + Zensical**: optional, only if you want to preview the documentation website locally.

### Getting the package into a Unity project

1. **Fork** and clone this repository.
2. Add it to a Unity project, either by:
    - cloning it into the project's `Packages/` folder (embedded package, recommended for development), or
    - referencing its Git URL from `Packages/manifest.json` (read-only, less convenient for editing).
3. Open the project in Unity. The package's code is declared through a `*.asmdef` file: a single runtime assembly for the `Runtime/` folder.

### Source layout

| Folder | Contents |
| --- | --- |
| `Runtime/` | Runtime code shipped with the package |
| `Documentation~/` | Documentation sources: guides, images and the Zensical config. The `~` suffix tells Unity to ignore this folder. |

### Documentation

The API reference under `Documentation~/api/` is **generated** from the C# source and is **not** committed (it's gitignored). It's produced by the [SideXP.Instadoc](https://www.nuget.org/packages/SideXP.Instadoc) CLI tool, which reads the source directly (no compilation, no DLL references needed).

To regenerate it locally:

```sh
# Install the tool once (global .NET tool)
dotnet tool install --global SideXP.Instadoc

# From the package root, generate the API docs into Documentation~/api/.
# This package ships runtime code only, so only the Runtime/ folder is scanned. Keep the --input
# folders in sync with the package's actual source folders (and with the release workflow).
instadoc \
  --input Runtime \
  --output "Documentation~/api" \
  --grouping namespace \
  --exclude "**/*.Generated.cs"
```

The documentation site's index page, `Documentation~/README.md`, is **generated from the repo-root [`README.md`](./README.md)** so the two never drift, and is therefore **not committed** (it's gitignored). CI rebuilds it on every docs build. The root README carries a few HTML-comment markers that the build interprets:

- `<!-- docs:remove:start -->` … `<!-- docs:remove:end -->`: content shown only on GitHub (e.g. the "Complete documentation available at …" pointer), stripped from the docs site index.
- `<!-- docs:only:start` … `docs:only:end -->`: content hidden on GitHub (it sits inside the comment) and shown only on the docs site — used to swap repo-relative links (`./CONTRIBUTING.md`, `./LICENSE.md`) for absolute ones that resolve on the published site.

The build also rewrites links that point into `./Documentation~/` (used on GitHub to reach the docs from the repo root) so they resolve on the published site, where the index already lives at the `Documentation~/` root.

The full documentation website (built from the Markdown files with [Zensical](https://zensical.org), using our [side-xp theme](https://github.com/side-xp/py-zensical-theme)) is assembled automatically in CI and deployed to GitHub Pages on each release. You don't need to build it to contribute. To preview it locally from the package root:

```sh
# Generate the index page from the root README (same transform CI runs)
sed -e '/<!-- docs:remove:start -->/,/<!-- docs:remove:end -->/d' \
    -e '/<!-- docs:only:start/d' \
    -e '/docs:only:end -->/d' \
    -e 's#\./Documentation~/#./#g' \
    README.md > "Documentation~/README.md"

pip install "git+https://github.com/side-xp/py-zensical-theme"
zensical serve -f "Documentation~/zensical.toml"
```

This serves the docs with live reload. To produce the static site instead, use `zensical build -f "Documentation~/zensical.toml"`, which writes it to the gitignored `Documentation~/site/` folder.

## Get involved!

There are many ways to be involved in our open source projects, and there's absolutely no pressure to give more or less of your time. So whether you want to develop the core of a whole package or just post a comment in an issue, any help is appreciated!

So what can you do to help?

- **Discuss with the community**: join our [Discord server](https://discord.gg/bMK2d47JaE), and start chatting with the community or the core team
- **Report bugs**: found a bug when using one of our packages? You can report it in the *Issues* tab on GitHub
- **Suggest improvements**: whether from the [Discord server](https://discord.gg/bMK2d47JaE) or by creating an *Issue*, feel free to talk about your needs or current usage of our solutions, highlight what's missing or what could be better
- **Request new features**: again, you can use the [Discord server](https://discord.gg/bMK2d47JaE) or create a new *Issue* to ask for something new, see if others may need it too, so we can consider modifying an existing package or even start a new project just for it
- **Address issues**: you can contribute directly to the codebase by resolving an issue, creating the required assets, or just implementing changes and create a Pull Request on *GitHub*

> By the way, we love to know how you use our tools, so we can make them better!

## Code of Conduct

*Sideways Experiments* has adopted the [*Contributor Covenant*](https://www.contributor-covenant.org/) as its Code of Conduct for its open source projects, and we expect contributors to adhere to it.

If you observe any unacceptable behavior or need to report a violation, please contact the *Sideways Experiments* core team directly to [contact@sideways-experiments.com](mailto:contact@sideways-experiments.com).

## Code syntax

Please refer to our [general Coding Style specification](https://github.com/side-xp/.github/blob/main/docs/coding-style/README.md) to learn more about the conventions used in this project.

## Submitting a Pull Request

To contribute code:

1. **Fork** this repository
2. Create a **new branch** off `develop`
3. Make your changes and commit them
   - **Signed commits** are required
   - Take care to follow our [commit message guidelines](https://github.com/side-xp/.github/blob/main/docs/coding-style/commit-messages.md)
4. Test your changes locally
5. Submit a **Pull Request targeting `develop` or `dev`**, not `master` or `main`
6. Describe your changes and reference any related issues
7. A member of the ***Sideways Experiments* core team** will review it

As mentioned in the [*Suggesting enhancements or features*](https://github.com/side-xp/.github/blob/main/SUPPORT.md#suggesting-enhancements-or-features) section of our support guidelines, please don't create Pull Requests for unsolicited work.

### Pull Request Requirements

- One PR per logical change (fix, feature, etc.)
- Must pass basic tests (if applicable)
- Must use **signed commits**
- All PRs must be reviewed by a *Sideways Experiments* core team member
- Assign the PR to the core team if not automatically assigned

## Releases

Releases are fully automated and handled internally (contributors don't need to do anything special beyond writing good commit messages).

We use [Conventional Commits](https://www.conventionalcommits.org/) together with [Release Please](https://github.com/googleapis/release-please):

1. When commits land on `main` (or `master`), **Release Please** opens and maintains a "release PR" that bumps the version in `package.json` and updates `CHANGELOG.md`, based on the commits since the last release:
    - `fix:` → patch (`x.y.Z`)
    - `feat:` → minor (`x.Y.z`)
    - `feat!:` or a `BREAKING CHANGE:` footer → major (`X.y.z`)
2. When the core team merges that release PR, a Git tag and a **GitHub Release** are created automatically.
3. Publishing the Release triggers our **Release & docs** workflow, which:
    - generates the API documentation from the source,
    - builds and deploys the documentation website to GitHub Pages,
    - attaches these assets to the Release:
        - `<package-name>.zip` / `<package-name>.tar.gz` (where `<package-name>` is the `name` field from `package.json`): the package source,
        - `Documentation_vX.Y.Z.zip`: a snapshot of the full Markdown documentation (including the generated API reference) for that exact version.

Because the version is derived from commit history, **following our [commit message guidelines](https://github.com/side-xp/.github/blob/main/docs/coding-style/commit-messages.md) is what drives the next version number**, so please take care with them.

## License and Contributor Agreement

Most of our projects are licensed under the [MIT License](https://mit-license.org).

A [`LICENSE.md`](./LICENSE.md) file is always included in our projects' repository root, and all contributions to a specific project will be licensed under the same terms as that file.

We will evaluate whether a Contributor License Agreement (CLA) is required in the future, as the projects go. For now, you can contribute freely under MIT.

## Maintainers

This project is maintained by the team at *Sideways Experiments*.

If you're unsure about any part of the contribution process, feel free to open a discussion on our [Discord server](https://discord.gg/bMK2d47JaE), or by sending an email directly to [contact@sideways-experiments.com](mailto:contact@sideways-experiments.com).

---

Thank you again for being part of the project!
