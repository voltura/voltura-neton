// Keep installer links current, with a working static fallback when GitHub's API is unavailable.
fetch('https://api.github.com/repos/voltura/voltura-neton/releases/latest', {headers: {Accept: 'application/vnd.github+json'}})
  .then(response => { if (!response.ok) throw new Error('Release lookup unavailable'); return response.json(); })
  .then(release => {
    const installer = release.assets?.find(asset => /^Voltura-NetOn-[\d.]+-Setup-win-x64\.exe$/.test(asset.name));
    if (!installer) return;
    const url = new URL(installer.browser_download_url);
    if (url.origin !== 'https://github.com' || !url.pathname.startsWith('/voltura/voltura-neton/releases/download/')) return;
    document.querySelectorAll('a.download').forEach(link => { link.href = url.href; });
  })
  .catch(() => {});
