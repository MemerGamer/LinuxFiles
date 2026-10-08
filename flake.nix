{
  description = "LinuxFiles: Files, a file manager, ported to native Linux (unofficial)";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { self, nixpkgs }:
    let
      # Only the x86_64 self-contained release tarball is published.
      system = "x86_64-linux";
      pkgs = import nixpkgs { inherit system; overlays = [ self.overlays.default ]; };
    in
    {
      overlays.default = final: prev: {
        linuxfiles = final.callPackage
          ({ lib, stdenv, fetchurl, autoPatchelfHook, makeWrapper
           , fontconfig, freetype, libGL, libx11, libxcursor, libxrandr, libxi, libxext
           , icu, openssl, zlib, krb5, glib }:
            let
              # version and both hashes are rewritten by scripts/linux/update-nix-flake.sh
              version = "0.1.0-alpha1";
              base = "https://github.com/MemerGamer/LinuxFiles/releases/download/linux-v${version}";
              app = fetchurl {
                url = "${base}/files-linux-x64.tar.gz";
                hash = "sha256-/mFEvG/q+gPRxMT8TORa/V7Cx4g2oDa9dGUz5iQK9q0=";
              };
              packaging = fetchurl {
                url = "${base}/files-packaging.tar.gz";
                hash = "sha256-lzSW7ixRJLxW9ZgDM79t6I52BkuB9sPZrrkxTfMCSro=";
              };
              # Opened with dlopen at runtime, so autoPatchelf cannot discover them; added to every ELF file's runpath.
              runtimeLibs = [
                fontconfig freetype libGL libx11 libxcursor libxrandr libxi libxext
                icu openssl zlib krb5 glib
              ];
              id = "io.github.memergamer.LinuxFiles";
            in
            stdenv.mkDerivation {
              pname = "linuxfiles";
              inherit version;

              dontUnpack = true;
              dontConfigure = true;
              dontBuild = true;
              # Stripping breaks the self-contained .NET binaries.
              dontStrip = true;

              nativeBuildInputs = [ autoPatchelfHook makeWrapper ];
              buildInputs = [ stdenv.cc.cc.lib fontconfig.lib zlib ];
              # The CoreCLR tracing provider needs LTTng, which the app never uses.
              autoPatchelfIgnoreMissingDeps = [ "liblttng-ust.so.0" ];
              # Appended to every ELF file so dlopen from any of them resolves (not exported via the environment).
              appendRunpaths = map (l: "${lib.getLib l}/lib") runtimeLibs;

              installPhase = ''
                runHook preInstall

                mkdir app packaging
                tar -xzf ${app} -C app --strip-components=1
                tar -xzf ${packaging} -C packaging

                mkdir -p $out/lib/linuxfiles $out/bin
                cp -a app/. $out/lib/linuxfiles/
                # The polkit root helper is not packaged for Nix; the marker turns all root actions off.
                rm -rf $out/lib/linuxfiles/elevation-helper
                touch $out/lib/linuxfiles/.root-actions-disabled
                # No library environment: it would leak into programs the app starts.
                makeWrapper $out/lib/linuxfiles/Files $out/bin/files

                install -Dm644 packaging/linux/${id}.desktop $out/share/applications/${id}.desktop
                install -Dm644 packaging/linux/${id}.metainfo.xml $out/share/metainfo/${id}.metainfo.xml
                for s in 16 24 32 48 64 128 256 512; do
                  install -Dm644 packaging/linux/icons/hicolor/''${s}x''${s}/apps/${id}.png \
                    $out/share/icons/hicolor/''${s}x''${s}/apps/${id}.png
                done
                install -Dm644 packaging/LICENSE-MIT $out/share/licenses/linuxfiles/LICENSE-MIT

                runHook postInstall
              '';

              meta = {
                description = "Unofficial Linux port of Files, a modern file manager (prebuilt release)";
                homepage = "https://github.com/MemerGamer/LinuxFiles";
                license = lib.licenses.mit;
                platforms = [ "x86_64-linux" ];
                sourceProvenance = [ lib.sourceTypes.binaryNativeCode ];
                mainProgram = "files";
              };
            })
          { };
      };

      packages.${system} = {
        linuxfiles = pkgs.linuxfiles;
        default = pkgs.linuxfiles;
      };

      apps.${system}.default = {
        type = "app";
        program = "${pkgs.linuxfiles}/bin/files";
      };
    };
}
