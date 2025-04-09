# NuGetCacheCleaner <img src="icon.png" width="36%" align="right" alt="Icon" />

![License](https://img.shields.io/badge/license-MIT-blue)

**Keep your local NuGet package cache nice and tidy!**<br/>
A tool for cleaning up your local NuGet package cache.

## Overview

NuGetCacheCleaner is a command-line tool designed to clean up your NuGet package cache by removing old and unused packages. It helps manage disk space by identifying packages that haven't been used for a specified period or are older versions of packages that are still in use.

## Installation

```
dotnet tool install --global NuGetCacheCleaner
```

## Usage

```
usage: dotnet nuget-cc [options]

Options:

  -c, --commit             Performs the actual clean-up. Default is to do a
                           dry-run and report the clean-up that would be done.

  -m, --min-days=VALUE     Number of days a package must not be used in order
                           to be purged from the cache. Defaults to 90.

  -v, --verbose            Verbose mode will display the paths directories that
                           would be removed.

  -p, --prune              Prune older versions of packages regardless of age.

  -?, -h, --help           Show this message.
```

## Features

- **Dry-run mode by default**: View what would be cleaned up without making any changes.
- **Age-based cleanup**: Remove packages unused for a specified number of days.
- **Version pruning**: Option to remove older versions of packages regardless of age.
- **Verbose output**: See detailed information about what's being removed.
- **Packaged as a .NET tool**: Easy to install and use globally.

## Examples

Check what would be cleaned up (dry run):
```sh
dotnet nuget-cc
```

Actually delete packages older than 90 days:
```sh
dotnet nuget-cc --commit
```

Delete packages unused for 30 days:
```sh
dotnet nuget-cc --commit --min-days=30
```

The all-in-one run, prune versions older than the latest and see detailed output:
```sh
dotnet nuget-cc --prune --verbose --commit
```

You can also use the short form of the options:
```sh
dotnet nuget-cc -pvc
```


## Contributing

Contributions are welcome!
Please fork the repository and create a pull request with your changes.
Be sure to follow the code style and include tests for any new features or bug fixes.

## License

This project is licensed under the MIT License.
See the [LICENSE.txt](LICENSE.txt) file for details.

## Acknowledgments

This project is based on the source of
[dotnet-nuget-gc](https://github.com/terrajobst/dotnet-nuget-gc)
by [@terrajobst (Immo Landwerth)](https://github.com/terrajobst).

It was originally created as a workaround for the
[missing cache-expiration policy](https://github.com/NuGet/Home/issues/4980) in NuGet
by [@dotMorten (Morten Nielsen)](https://github.com/dotMorten),
which he wrote as a [code snippet in a comment in this issue](https://github.com/NuGet/Home/issues/4980#issuecomment-432512640).

