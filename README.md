DepotDumper
===============

Mass depot key dumper utilizing the SteamKit2 library. Supports .NET 9.0

This program must be run from a console, it has no GUI.

Resulting files:

**{username}_appnames.txt**
* Contains names of all apps and depots owned by the account.

**{username}_apps.txt**
* Contains app tokens in "appId;appToken" format.

**{username}_keys.txt**
* Contains depot keys in "depotId;depotKey" format.

**{username}_pkgs.txt**
* Contains package tokens in "pkgId;pkgToken" format.

Optional parameters:
* -app \<#> - dump keys for a specific app.
* -apikey <key> - fetch existing depot IDs from a database to avoid re-dumping keys that are already there.
* -dump-unreleased - apps that don't have "released" status are skipped by default to prevent accidental leaks, add this parameter to override this behavior.
