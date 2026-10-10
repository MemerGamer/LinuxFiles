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
           , icu, openssl, zlib, krb5, glib, enableRootActions ? false }:
            let
              # version and both hashes are rewritten by scripts/linux/update-nix-flake.sh
              version = "0.1.0-alpha4";
              base = "https://github.com/MemerGamer/LinuxFiles/releases/download/linux-v${version}";
              app = fetchurl {
                url = "${base}/files-linux-x64.tar.gz";
                hash = "sha256-9xbWW/eMsntdPL9oLc+Y08PoTcPo2X8F2FtNarL3Q8E=";
              };
              packaging = fetchurl {
                url = "${base}/files-packaging.tar.gz";
                hash = "sha256-9sT8ClqulxECpVzEEdtph+StyP3i4gl74Y66EW+MmPg=";
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
              # Alpha3 includes NixOS deployment-manifest and elevation-wrapper discovery.
              passthru.supportsNixRootActions = true;

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
                # Keep the AOT helper so autoPatchelf patches its loader and dependencies too.
                test -x $out/lib/linuxfiles/elevation-helper/files-elevation-helper
                ${if enableRootActions then ''
                  rm -f $out/lib/linuxfiles/.root-actions-disabled
                  install -Dm644 ${./packaging/linux/io.github.memergamer.LinuxFiles.root-actions.policy} \
                    $out/share/polkit-1/actions/${id}.root-actions.policy
                  substituteInPlace $out/share/polkit-1/actions/${id}.root-actions.policy \
                    --replace-fail /usr/lib/linuxfiles/files-elevation-helper \
                    $out/lib/linuxfiles/elevation-helper/files-elevation-helper
                '' else ''
                  touch $out/lib/linuxfiles/.root-actions-disabled
                ''}
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

      nixosModules.default = { config, lib, pkgs, options, ... }:
        let
          cfg = config.programs.linuxfiles;
          package = cfg.package.override { enableRootActions = cfg.rootActions; };
          helper = "${package}/lib/linuxfiles/elevation-helper/files-elevation-helper";
          policy = "${package}/share/polkit-1/actions/io.github.memergamer.LinuxFiles.root-actions.policy";
        in
        {
          options.programs.linuxfiles = {
            enable = lib.mkEnableOption "LinuxFiles";
            package = lib.mkOption {
              type = lib.types.package;
              default = self.packages.${pkgs.stdenv.hostPlatform.system}.linuxfiles;
              description = "LinuxFiles package accepting the enableRootActions override.";
            };
            rootActions = lib.mkEnableOption "polkit-authenticated LinuxFiles root operations";
          };
          config = lib.mkIf cfg.enable (lib.mkMerge [
            {
              environment.systemPackages = [ package ];
              assertions = [{
                assertion = !cfg.rootActions || (package.passthru.supportsNixRootActions or false);
                message = "programs.linuxfiles.rootActions requires a runtime with NixOS deployment-manifest and elevation-wrapper discovery (passthru.supportsNixRootActions = true). Disable rootActions or select a compatible package.";
              }];
            }
            (lib.mkIf cfg.rootActions (lib.mkMerge [
              {
                security.polkit.enable = true;
                environment.etc."linuxfiles/root-actions".text = "${helper}\n${policy}\n";
              }
              # Older nixpkgs creates pkexec with polkit.enable; newer revisions gate it separately.
              (if options.security.polkit ? enablePkexecWrapper then {
                security.polkit.enablePkexecWrapper = true;
              } else {
                security.wrappers.pkexec = {
                  source = "${pkgs.polkit.bin}/bin/pkexec";
                  owner = "root";
                  group = "root";
                  setuid = true;
                };
              })
            ]))
          ]);
        };

      checks.${system}.root-actions-module =
        let
          compatible = self.packages.${system}.linuxfiles;
          unsupported = nixpkgs.lib.nixosSystem {
            inherit system;
            modules = [ self.nixosModules.default {
              programs.linuxfiles.enable = true;
              programs.linuxfiles.rootActions = true;
              programs.linuxfiles.package = compatible.overrideAttrs (old: {
                passthru = (old.passthru or { }) // { supportsNixRootActions = false; };
              });
            } ];
          };
          unmarked = nixpkgs.lib.nixosSystem {
            inherit system;
            modules = [ self.nixosModules.default {
              programs.linuxfiles.enable = true;
              programs.linuxfiles.rootActions = true;
              programs.linuxfiles.package = compatible.overrideAttrs (old: {
                passthru = builtins.removeAttrs old.passthru [ "supportsNixRootActions" ];
              });
            } ];
          };
          capabilityAssertion = evaluated:
            nixpkgs.lib.findFirst
              (entry: nixpkgs.lib.hasPrefix "programs.linuxfiles.rootActions requires" entry.message)
              (throw "Missing root-actions runtime assertion") evaluated.config.assertions;
          enabled = nixpkgs.lib.nixosSystem {
            inherit system;
            modules = [ self.nixosModules.default {
              programs.linuxfiles.enable = true;
              programs.linuxfiles.rootActions = true;
              programs.linuxfiles.package = compatible;
            } ];
          };
          disabled = nixpkgs.lib.nixosSystem {
            inherit system;
            modules = [ self.nixosModules.default { programs.linuxfiles.enable = true; } ];
          };
          # Pick our package explicitly; other modules also add system packages.
          package = compatible.override { enableRootActions = true; };
        in
        assert !(capabilityAssertion unsupported).assertion;
        assert !(capabilityAssertion unmarked).assertion;
        assert (capabilityAssertion enabled).assertion;
        assert (capabilityAssertion disabled).assertion;
        assert enabled.config.security.polkit.enable;
        assert enabled.config.security.wrappers.pkexec.setuid;
        assert enabled.config.environment.etc."linuxfiles/root-actions".text ==
          "${package}/lib/linuxfiles/elevation-helper/files-elevation-helper\n${package}/share/polkit-1/actions/io.github.memergamer.LinuxFiles.root-actions.policy\n";
        assert !(disabled.config.environment.etc ? "linuxfiles/root-actions");
        assert !disabled.config.security.polkit.enable;
        pkgs.runCommand "linuxfiles-root-actions-layout" { } ''
          test -x ${package}/lib/linuxfiles/elevation-helper/files-elevation-helper
          test ! -e ${package}/lib/linuxfiles/.root-actions-disabled
          grep -F '<annotate key="org.freedesktop.policykit.exec.path">${package}/lib/linuxfiles/elevation-helper/files-elevation-helper</annotate>' \
            ${package}/share/polkit-1/actions/io.github.memergamer.LinuxFiles.root-actions.policy
          test -e ${self.packages.${system}.linuxfiles}/lib/linuxfiles/.root-actions-disabled
          touch $out
        '';

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
