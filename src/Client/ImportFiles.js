// Reads bounded local files as base64 while preserving their exact bytes.
export async function readFiles(fileList) {
  const files = Array.from(fileList || []);
  if (!files.length || files.length > 20) throw new Error('Choose 1 to 20 files.');
  if (files.reduce((sum, file) => sum + file.size, 0) > 10000000) throw new Error('Imports are limited to 10 MB in total.');
  return Promise.all(files.map(async file => {
    const bytes = new Uint8Array(await file.arrayBuffer());
    let binary = '';
    for (let start = 0; start < bytes.length; start += 32768) {
      binary += String.fromCharCode(...bytes.subarray(start, start + 32768));
    }
    return { Name: file.name, ContentBase64: btoa(binary) };
  }));
}
