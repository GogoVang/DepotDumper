// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using SteamKit2;
using System.Threading.Tasks;

namespace DepotDumper
{
    class Program
    {
        public static DumperConfig Config = new();
        private static Steam3Session steam3;
        private static readonly HttpClient httpClient = new HttpClient();

        public class ApiResponse
        {
            public string status { get; set; }
            public int total_depot_ids { get; set; }
            public int existing_count { get; set; }
            public List<string> depot_ids { get; set; }
            public string timestamp { get; set; }
        }

        static async Task<int> Main( string[] args )
        {
            string user = null;
            string password = null;
            Config.UseQrCode = HasParameter( args, "-qr" );
            Config.RememberPassword = false;
            Config.TargetAppId = GetParameter<uint>( args, "-app", uint.MaxValue );
            Config.DumpUnreleased = HasParameter( args, "-dump-unreleased" );

            string apiKey = GetParameter<string>( args, "-apikey", null );
            HashSet<uint> existingDepotIds = null;
            bool hasApiKey = false;

            if ( !string.IsNullOrWhiteSpace( apiKey ) )
            {
                Console.WriteLine( "Fetching existing depot IDs from API..." );
                existingDepotIds = await FetchExistingDepotIds( apiKey );

                if ( existingDepotIds != null && existingDepotIds.Count > 0 )
                {
                    Console.WriteLine( "Loaded {0} depot IDs already in database", existingDepotIds.Count );
                    hasApiKey = true;
                }
                else
                {
                    Console.WriteLine( "Failed to fetch depot IDs from API - API key may be invalid" );
                    Console.Write( "Continue dumping without API key filtering? (yes/no): " );
                    string response = Console.ReadLine()?.Trim().ToLower();
                    
                    if ( response != "yes" && response != "y" )
                    {
                        Console.WriteLine( "Exiting..." );
                        return 1;
                    }
                    
                    Console.WriteLine( "Continuing without API key filtering..." );
                    existingDepotIds = null;
                }
            }

            if ( !Config.UseQrCode )
            {
                Console.Write( "Username: " );
                user = Console.ReadLine();

                if ( string.IsNullOrWhiteSpace( user ) )
                {
                    Console.WriteLine( "Username is required. Anonymous login is not supported." );
                    return 1;
                }

                do
                {
                    Console.Write( "Password: " );
                    if ( Console.IsInputRedirected )
                    {
                        password = Console.ReadLine();
                    }
                    else
                    {
                        // Avoid console echoing of password
                        password = Util.ReadPassword();
                    }

                    Console.WriteLine();
                } while ( string.Empty == password );
            }

            AccountSettingsStore.LoadFromFile( "xxx" );

            steam3 = new Steam3Session(
               new SteamUser.LogOnDetails()
               {
                   Username = user,
                   Password = password,
                   ShouldRememberPassword = false,
                   LoginID = 0x534B32, // "SK2"
               }
            );

            if ( !steam3.WaitForCredentials() )
            {
                Console.WriteLine( "Unable to get steam3 credentials." );
                return 1;
            }

            Console.WriteLine( "Getting licenses..." );
            steam3.WaitUntilCallback( () => { }, () => { return steam3.Licenses != null; } );
            var licenseQuery = steam3.Licenses.Select( x => x.PackageID ).Distinct();

            await steam3.RequestPackageInfo( licenseQuery );

            if ( Config.TargetAppId == uint.MaxValue )
            {
                StreamWriter sw_pkgs = new StreamWriter( string.Format( "{0}_pkgs.txt", user ) );
                sw_pkgs.AutoFlush = true;

                // Collect all apps user owns.
                var apps = new List<uint>();

                foreach ( var license in licenseQuery )
                {
                    SteamApps.PICSProductInfoCallback.PICSProductInfo package;
                    if ( steam3.PackageInfo.TryGetValue( license, out package ) && package != null )
                    {
                        var token = steam3.PackageTokens.ContainsKey( license ) ? steam3.PackageTokens[license] : 0;
                        sw_pkgs.WriteLine( "{0};{1}", license, token );

                        List<KeyValue> packageApps = package.KeyValues["appids"].Children;
                        apps.AddRange( packageApps.Select( x => x.AsUnsignedInteger() ).Where( x => !apps.Contains( x ) ) );
                    }
                }

                sw_pkgs.Close();

                StreamWriter sw_apps = new StreamWriter( string.Format( "{0}_apps.txt", user ) );
                sw_apps.AutoFlush = true;
                StreamWriter sw_keys = new StreamWriter( string.Format( "{0}_keys.txt", user ) );
                sw_keys.AutoFlush = true;
                StreamWriter sw_appnames = new StreamWriter( string.Format( "{0}_appnames.txt", user ) );
                sw_appnames.AutoFlush = true;

                await steam3.RequestAppInfoList( apps );

                var depots = new List<uint>();
                int dumpedCount = 0;
                int skippedCount = 0;

                foreach ( var appId in apps )
                {
                    var result = await DumpApp( appId, licenseQuery, sw_apps, sw_keys, sw_appnames, depots, existingDepotIds );
                    dumpedCount += result.dumped;
                    skippedCount += result.skipped;
                }

                sw_apps.Close();
                sw_keys.Close();
                sw_appnames.Close();

                Console.WriteLine( "\n=== Summary ===" );
                if ( hasApiKey )
                {
                    Console.WriteLine( "Dumped: {0} new depot keys", dumpedCount );
                    Console.WriteLine( "Skipped: {0} depot keys (already in database)", skippedCount );
                }
                else
                {
                    Console.WriteLine( "Dumped: {0} depot keys", dumpedCount );
                }
            }
            else
            {
                await steam3.RequestAppInfo( Config.TargetAppId );

                if ( steam3.AppTokens.ContainsKey( Config.TargetAppId ) )
                {
                    StreamWriter sw_apps = new StreamWriter( string.Format( "app_{0}_token.txt", Config.TargetAppId ) );
                    StreamWriter sw_keys = new StreamWriter( string.Format( "app_{0}_keys.txt", Config.TargetAppId ) );
                    StreamWriter sw_appnames = new StreamWriter( string.Format( "app_{0}_names.txt", Config.TargetAppId ) );

                    var result = await DumpApp( Config.TargetAppId, licenseQuery, sw_apps, sw_keys, sw_appnames, new List<uint>(), existingDepotIds );

                    sw_apps.Close();
                    sw_keys.Close();
                    sw_appnames.Close();

                    Console.WriteLine( "\n=== Summary ===" );
                    if ( hasApiKey )
                    {
                        Console.WriteLine( "Dumped: {0} new depot keys", result.dumped );
                        Console.WriteLine( "Skipped: {0} depot keys (already in database)", result.skipped );
                    }
                    else
                    {
                        Console.WriteLine( "Dumped: {0} depot keys", result.dumped );
                    }
                }
            }

            steam3.Disconnect();

            return 0;
        }

        static async Task<HashSet<uint>> FetchExistingDepotIds( string apiKey )
        {
            try
            {
                string url = $"https://manifest.morrenus.xyz/api/v1/depot-keys?api_key={apiKey}";

                var response = await httpClient.GetAsync( url );

                if ( !response.IsSuccessStatusCode )
                {
                    Console.WriteLine( "API request failed with status code: {0}", response.StatusCode );
                    return null;
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var apiResponse = JsonSerializer.Deserialize<ApiResponse>( jsonResponse, options );

                if ( apiResponse == null || apiResponse.status != "success" )
                {
                    Console.WriteLine( "API returned unsuccessful status" );
                    return null;
                }

                Console.WriteLine( "API: {0} existing in DB, {1} missing (as of {2})",
                    apiResponse.existing_count, apiResponse.depot_ids?.Count ?? 0, apiResponse.timestamp );


                var existingIds = new HashSet<uint>();

                if ( apiResponse.depot_ids != null )
                {
                    foreach ( var depotIdStr in apiResponse.depot_ids )
                    {
                        if ( uint.TryParse( depotIdStr, out uint depotId ) )
                        {
                            existingIds.Add( depotId );
                        }
                    }
                }

                return existingIds;
            }
            catch ( Exception ex )
            {
                Console.WriteLine( "Error fetching depot IDs from API: {0}", ex.Message );
                return null;
            }
        }

        static async Task<(int dumped, int skipped)> DumpApp( uint appId, IEnumerable<uint> licenses,
            StreamWriter sw_apps, StreamWriter sw_keys, StreamWriter sw_appnames,
            List<uint> depots, HashSet<uint> existingDepotIds = null )
        {
            int dumpedCount = 0;
            int skippedCount = 0;

            SteamApps.PICSProductInfoCallback.PICSProductInfo app;
            if ( !steam3.AppInfo.TryGetValue( appId, out app ) || app == null )
                return (0, 0);

            if ( !steam3.AppTokens.ContainsKey( appId ) )
                return (0, 0);

            KeyValue appInfo = app.KeyValues;
            KeyValue depotInfo = appInfo["depots"];

            if ( !Config.DumpUnreleased &&
                appInfo["common"]["ReleaseState"] != KeyValue.Invalid &&
                appInfo["common"]["ReleaseState"].AsString() != "released" )
                return (0, 0);

            sw_apps.WriteLine( "{0};{1}", appId, steam3.AppTokens[appId] );
            sw_appnames.WriteLine( "{0} - {1}", appId, appInfo["common"]["name"].AsString() );

            if ( depotInfo == KeyValue.Invalid )
                return (0, 0);

            foreach ( var depotSection in depotInfo.Children )
            {
                uint depotId = uint.MaxValue;

                if ( !uint.TryParse( depotSection.Name, out depotId ) || depotId == uint.MaxValue )
                    continue;

                if ( depotSection["manifests"] == KeyValue.Invalid )
                {
                    if ( depotSection["depotfromapp"] != KeyValue.Invalid )
                    {
                        uint otherAppId = depotSection["depotfromapp"].AsUnsignedInteger();
                        if ( otherAppId == appId )
                        {
                            // This shouldn't ever happen, but ya never know with Valve.
                            Console.WriteLine( "App {0}, Depot {1} has depotfromapp of {2}!",
                                appId, depotId, otherAppId );
                            continue;
                        }

                        await steam3.RequestAppInfo( otherAppId );

                        SteamApps.PICSProductInfoCallback.PICSProductInfo otherApp;
                        if ( !steam3.AppInfo.TryGetValue( otherAppId, out otherApp ) || otherApp == null )
                            continue;

                        if ( otherApp.KeyValues["depots"][depotId.ToString()]["manifests"] == KeyValue.Invalid )
                            continue;
                    }
                    else
                    {
                        continue;
                    }
                }

                bool isOwned = false;
                foreach ( var license in licenses )
                {
                    SteamApps.PICSProductInfoCallback.PICSProductInfo package;
                    if ( steam3.PackageInfo.TryGetValue( license, out package ) && package != null )
                    {
                        // Check app list, too, since owning an app with the same ID counts as owning the depot.
                        if ( package.KeyValues["depotids"].Children.Any( child => child.AsUnsignedInteger() == depotId ) ||
                            package.KeyValues["appids"].Children.Any( child => child.AsUnsignedInteger() == depotId ) )
                        {
                            isOwned = true;
                            break;
                        }
                    }
                }

                if ( !isOwned )
                    continue;

                if ( existingDepotIds != null && existingDepotIds.Contains( depotId ) )
                {
                    if ( !depots.Contains( depotId ) )
                    {
                        depots.Add( depotId );
                        skippedCount++;
                    }

                    sw_appnames.WriteLine( "\t{0}", depotId );

                    if ( depotSection["manifests"] != KeyValue.Invalid )
                    {
                        foreach ( var branch in depotSection["manifests"].Children )
                        {
                            sw_appnames.WriteLine( "\t\t{0} - {1}", branch.Name, branch["gid"].AsUnsignedLong() );
                        }
                    }

                    continue;
                }

                await steam3.RequestDepotKeyEx( depotId, appId );

                byte[] depotKey;
                if ( steam3.DepotKeys.TryGetValue( depotId, out depotKey ) )
                {
                    if ( !depots.Contains( depotId ) )
                    {
                        string keyHex = string.Concat( depotKey.Select( b => b.ToString( "X2" ) ).ToArray() );
                        sw_keys.WriteLine( "{0};{1}", depotId, keyHex );
                        depots.Add( depotId );
                        dumpedCount++;
                    }

                    sw_appnames.WriteLine( "\t{0}", depotId );

                    if ( depotSection["manifests"] != KeyValue.Invalid )
                    {
                        foreach ( var branch in depotSection["manifests"].Children )
                        {
                            sw_appnames.WriteLine( "\t\t{0} - {1}", branch.Name, branch["gid"].AsUnsignedLong() );
                        }
                    }
                }
            }

            // Workshop depot
            if ( depotInfo["workshopdepot"] != KeyValue.Invalid )
            {
                uint workshopDepotId = depotInfo["workshopdepot"].AsUnsignedInteger();
                if ( workshopDepotId != 0 && !depots.Contains( workshopDepotId ) )
                {
                    if ( existingDepotIds != null && existingDepotIds.Contains( workshopDepotId ) )
                    {
                        Console.WriteLine( "Workshop depot {0} already exists in database, skipping", workshopDepotId );
                        depots.Add( workshopDepotId );
                        sw_appnames.WriteLine( "\t{0} (workshop - already in DB)", workshopDepotId );
                        skippedCount++;
                    }
                    else
                    {
                        await steam3.RequestDepotKeyEx( workshopDepotId, appId );

                        byte[] workshopKey;
                        if ( steam3.DepotKeys.TryGetValue( workshopDepotId, out workshopKey ) )
                        {
                            sw_keys.WriteLine( "{0};{1}", workshopDepotId, string.Concat( workshopKey.Select( b => b.ToString( "X2" ) ).ToArray() ) );
                            depots.Add( workshopDepotId );
                            sw_appnames.WriteLine( "\t{0} (workshop)", workshopDepotId );
                            Console.WriteLine( "Dumped workshop depot key for depot {0}", workshopDepotId );
                            dumpedCount++;
                        }
                    }
                }
            }

            return (dumpedCount, skippedCount);
        }

        static int IndexOfParam( string[] args, string param )
        {
            for ( int x = 0; x < args.Length; ++x )
            {
                if ( args[x].Equals( param, StringComparison.OrdinalIgnoreCase ) )
                    return x;
            }
            return -1;
        }

        static bool HasParameter( string[] args, string param )
        {
            return IndexOfParam( args, param ) > -1;
        }

        static T GetParameter<T>( string[] args, string param, T defaultValue = default( T ) )
        {
            int index = IndexOfParam( args, param );

            if ( index == -1 || index == ( args.Length - 1 ) )
                return defaultValue;

            string strParam = args[index + 1];

            var converter = TypeDescriptor.GetConverter( typeof( T ) );
            if ( converter != null )
            {
                return (T)converter.ConvertFromString( strParam );
            }

            return default( T );
        }
    }
}
