function navispan(text, link) {
  return (link != null && link.length > 0)
    ? "<span><a href='" + link + "'>" + text + "</a></span>"
    : "<span style='color:gray'>" + text + "</span>";
}

function navigation(edat, home, prev, next) {
  var obj = document.getElementById("pagehdr");
  if(obj != null) obj.innerHTML =
    "<h1 class='function' align='center'>" + document.title + "</h1>" +
    "<div align='center'><i>Geschrieben " + edat + " von Dr. J&uuml;rgen Pfennig &copy; 2008 " +
    "<a href='http://www.gnu.org/licenses/fdl.html'>(GNU Free Documentation License)</a></i></div>"

  if(home == null) {
     //-------------------------------------------------------------------------
     var hom = "www.j-pfennig.de"; // used for cur == idx
     var idx = "../LinuxImHaus/index";  // used for 'home' link
     var lst = new Array("zimap");
     //-------------------------------------------------------------------------
     var i1 = document.URL.lastIndexOf("\\"); // for windows
     if(i1 < 0) i1 = document.URL.lastIndexOf("/");
     var i2 = document.URL.lastIndexOf(".");
     var cur = document.URL.substring(i1+1, i2);
     home = ((cur == idx) ? hom + "/" + idx : idx) + ".html";
     for(i1=0 ; i1 < lst.length; i1++)
     {  if(lst[i1] == cur)
        {  if(i1+1 < lst.length) next = lst[i1+1] + ".html";
           if(i1 > 0) prev = lst[i1-1] + ".html"; break;
     }  }
  }

  var nav =  "<div class='navi' align='center'>"
    + navispan("home", home) + " "
    + navispan("previous", prev) + " "
    + navispan("next", next) + "</div>";
  obj = document.getElementById("navitop");  if(obj != null) obj.innerHTML = nav;
  obj = document.getElementById("navibot");  if(obj != null) obj.innerHTML = nav;
}
