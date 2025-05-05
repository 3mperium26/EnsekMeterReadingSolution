// Example: Initialize Bootstrap tooltips (if you use them)

document.addEventListener('DOMContentLoaded', function () {
  var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'))
  var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
    return new bootstrap.Tooltip(tooltipTriggerEl)
  })
});


// Example: Add simple feedback on file selection (optional)

const fileInput = document.getElementById('meterReadingFile');
if (fileInput) {
    fileInput.addEventListener('change', function() {
        if (this.files && this.files.length > 0) {
            console.log(`File selected: ${this.files[0].name}`);
            // Could potentially display the filename somewhere
        } else {
            console.log('File selection cleared.');
        }
    });
}