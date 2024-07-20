# Publish the self-contained application
dotnet publish -c Release -r linux-x64 --self-contained true

# Create the new self-contained executable with warp-packer
./warp-packer --arch linux-x64 --input_dir ./bin/Release/net8.0/linux-x64/publish --exec OpenDirectoryDownloader --output OpenDirectoryDownloader-linux

# Run the new executable with PostgreSQL options
# ./OpenDirectoryDownloader-linux --postgres --postgres-connection "Host=localhost;Username=postgres;Password=mysecretpassword;Database=postgres"

# https://glotaran.org/nb-mirror/maven2-partial-mirror/org/netbeans/api/org-netbeans-modules-jellytools-cnd/RELEASE802/
# CREATE TABLE site_inventory (
#     id serial NOT NULL,
#     url text NOT NULL UNIQUE,
#     file_name text NOT NULL,
#     file_extension text NOT NULL,
#     last_modified timestamp without time zone NOT NULL,
#     initialized boolean DEFAULT false
# );