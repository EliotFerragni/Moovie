# Runs Moovie's web mode on a machine with no screen.
#
# The binary is built outside this file, by build/publish.sh, and only copied in. So the image
# needs no .NET SDK, and the machine it is built for never sees the source. The build context is
# the published output directory, which holds exactly one file.
#
# There is no X11 here and no desktop: --web draws into a frame buffer and serves the result over
# HTTP. The runtime-deps image (Ubuntu based) already carries what .NET itself needs, including
# ICU, which this app does require, since it does not use invariant globalization.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0

# Both of these were established by running the binary in the bare base image and reading what it
# complained about. Without libfontconfig1, libSkiaSharp.so will not load at all. With it but with
# no font installed, Skia loads and then Avalonia fails with "Default font family name can't be
# null or empty" — the app embeds Inter for its own text, but the font manager still needs a
# system default to fall back on.
RUN apt-get update && apt-get install -y --no-install-recommends \
      libfontconfig1 \
      fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

COPY Moovie /usr/local/bin/Moovie
RUN chmod +x /usr/local/bin/Moovie

# Settings live in $XDG_CONFIG_HOME/Moovie, so pointing that at a volume is what makes the TMDB
# key and the rename templates survive the container being replaced.
#
# The extraction directory has to be set explicitly. A single-file build unpacks itself to
# $HOME/.net on startup, and with HOME pointing at /config that made the app refuse to start at
# all whenever /config was not writable by the user the container runs as, which on a NAS it
# usually is not. /tmp is writable by anyone, and holds nothing worth keeping.
ENV XDG_CONFIG_HOME=/config \
    HOME=/config \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/.net

# Left writable by whatever user the container is given, so a first run still starts when the
# volumes have not been mounted yet.
RUN mkdir -p /config /media && chmod 777 /config /media

VOLUME ["/config", "/media"]
EXPOSE 8080

# The arguments live in CMD rather than ENTRYPOINT so that passing any of your own replaces them
# outright. Baked into ENTRYPOINT, a second --port would be appended to the first rather than
# replacing it, and `docker run moovie --help` could not work at all.
ENTRYPOINT ["/usr/local/bin/Moovie"]
CMD ["--web", "--port", "8080", "/media"]
