async function AJAXSubmit() {
  const maxfilesize = 1024 * 1024 * 2; // 1 Mb
  const el = document.getElementById("FileUpload_FormFile");
  const filesize = el.files[0].size;

  if (filesize > maxfilesize) {
    alert("Demo: Max size allowed is 2mb");
    return false;
  }

  const resultElement = document.querySelector("#formUpload");
  const formData = new FormData(resultElement);

  try {
    const resp = await fetch("/Home/UploadFile", {
      method: "POST",
      body: formData,
    });
    if (resp.status === 200) {
      const blob = await resp.blob();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.style.display = "none";
      a.href = url;
      // the filename you want
      a.download = "files.zip";
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      alert("your file has downloaded!");
    } else if (resp.status === 400) {
      const errors = await resp.text();
      alert(errors);
    }
  } catch (error) {
    console.error("Error:", error);
  }
}

const file = document.getElementById("FileUpload_FormFile");

file.onchange = function (e) {
  const ext = this.value.match(/\.([^\.]+)$/)[1];
  if (ext !== "txt") {
    alert("Demo: .txt files only");
    this.value = "";
  }
};
