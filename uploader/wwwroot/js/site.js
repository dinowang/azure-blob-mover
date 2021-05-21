// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
$(document).ready(function () {

    $("html")
        .on("dragover", function (e) {
            e.preventDefault();
            e.stopPropagation();
        });

    $(".dropfile")
        .on("dragenter", function (e) {
            e.preventDefault();
            e.stopPropagation();

            var $this = $(this);
            if (!$this.is(".over")) {
                $this.addClass("over");
            }
        })
        .on("dragleave", function (e) {
            e.preventDefault();
            e.stopPropagation();

            var $this = $(this);
            $this.removeClass("over");
        })
        .on("drop", function (e) {
            e.stopPropagation();
            e.preventDefault();

            var $this = $(this),
                data = new FormData();

            $.each(e.originalEvent.dataTransfer.files, function (i, file) {
                data.append(file.name, file);
            });

            $.ajax({
                type: "POST",
                url: $this.data("upload-url"),
                contentType: false,
                processData: false,
                data: data,
                success: function (result, status, xhr) {
                    $this.removeClass("over");
                    $.each(result, (i, x) => {
                        $(`<li class="list-group-item text-truncate"><span class="float-right">${x.time}</span><span class="text-left">${x.message}</span></li>`).prependTo(".uploads");
                    });
                },
                error: function (result, status, xhr) {
                    $this.removeClass("over");
                }
            });
        });
});
