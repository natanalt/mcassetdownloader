# mcassetdownloader
Mcassetdownloader is a simple C# console application used to download Minecraft assets directly from Mojang servers and pack them into a resource pack zip archive.

## Usage
Run `Mcassetdownloader.exe [version] [target zip path]`, e.g. `Mcassetdownloader.exe 1.16.3 resources.zip` to begin the download and packaging. If the archive already exists, you'll be asked whether you want to overwrite it. The process might take a while, depending on your network and machine speed.

Note, that the program might use a lot of RAM. In my tests the usage never reached 500 MB, though. Also note, that there's basically no error handling, so if anything goes wrong, bad things may happen.

## License stuff
So, I guess I need a license for this, eh? Let's go with MIT License, see [LICENSE](LICENSE). Also, this program uses [Newtonsoft.Json](https://www.newtonsoft.com/json) for JSON parsing.
